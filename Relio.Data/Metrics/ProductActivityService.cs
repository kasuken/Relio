using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Relio.Application.Metrics;
using Relio.Application.Security;
using Relio.Data.Configurations;
using Relio.Data.Identity;
using Relio.Domain;

namespace Relio.Data.Metrics;

/// <summary>
/// Records one minimal, pseudonymous activity contribution for the current active user when
/// instance metrics are explicitly enabled.
/// </summary>
public sealed class ProductActivityService(
    RelioDbContext dbContext,
    ICurrentUser currentUser,
    IOptionsMonitor<ProductMetricsOptions> options,
    IProductMetricsCollectionGate collectionGate,
    TimeProvider timeProvider) : IProductActivityService
{
    private readonly RelioDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly ICurrentUser _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    private readonly IOptionsMonitor<ProductMetricsOptions> _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IProductMetricsCollectionGate _collectionGate =
        collectionGate ?? throw new ArgumentNullException(nameof(collectionGate));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public async Task RecordAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = _currentUser.RequireUserId();
        if (!_options.CurrentValue.Enabled)
        {
            return;
        }

        using var collectionLease = await _collectionGate.EnterAsync(cancellationToken);
        if (!_options.CurrentValue.Enabled)
        {
            return;
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var todayUtc = DateOnly.FromDateTime(utcNow);

        try
        {
            if (!await IsActiveAccountAsync(ownerId, cancellationToken))
            {
                return;
            }

            await RecordForActiveAccountAsync(ownerId, todayUtc, utcNow, cancellationToken);
        }
        catch (DbUpdateException exception)
            when (SqlServerErrors.IsUniqueIndexViolation(exception, ProductActivityConfiguration.OwnerIndexName))
        {
            // Only the one-row-per-owner unique index is a recoverable create/reset race. The SQL
            // exception contains the conflicting owner key, so it is never retained or rethrown.
            _dbContext.ChangeTracker.Clear();
            await RecoverOwnerRaceAsync(ownerId, todayUtc, utcNow, cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ProductActivityWriteException();
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    private Task<bool> IsActiveAccountAsync(string ownerId, CancellationToken cancellationToken) =>
        _dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == ownerId && !user.IsDisabled, cancellationToken);

    private async Task RecordForActiveAccountAsync(
        string ownerId,
        DateOnly todayUtc,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var activities = _dbContext.Set<ProductActivity>();
        var activity = await activities
            .FirstOrDefaultAsync(candidate => candidate.OwnerId == ownerId, cancellationToken);

        await ApplyActivityAsync(activity, ownerId, todayUtc, utcNow, cancellationToken);
    }

    private async Task ApplyActivityAsync(
        ProductActivity? activity,
        string ownerId,
        DateOnly todayUtc,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var activities = _dbContext.Set<ProductActivity>();
        if (activity is null || ProductActivityCohortRules.IsExpired(activity, utcNow))
        {
            if (activity is not null)
            {
                // Replace rather than reuse an expired row so its CreatedAtUtc and identifier do
                // not carry creation history into the new observation cohort.
                activities.Remove(activity);
            }

            activities.Add(NewActivity(ownerId, todayUtc));
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var changed = false;
        if (todayUtc > activity.LastActiveOnUtc)
        {
            activity.LastActiveOnUtc = todayUtc;
            changed = true;
        }

        if (!activity.ReturnedInDays30To59
            && ProductActivityCohortRules.IsWithinReturnWindow(activity.CohortStartedOnUtc, todayUtc))
        {
            activity.ReturnedInDays30To59 = true;
            changed = true;
        }

        if (changed)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task RecoverOwnerRaceAsync(
        string ownerId,
        DateOnly todayUtc,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await IsActiveAccountAsync(ownerId, cancellationToken))
            {
                return;
            }

            var activity = await _dbContext.Set<ProductActivity>()
                .FirstOrDefaultAsync(candidate => candidate.OwnerId == ownerId, cancellationToken);
            await ApplyActivityAsync(activity, ownerId, todayUtc, utcNow, cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ProductActivityWriteException();
        }
    }

    private static ProductActivity NewActivity(string ownerId, DateOnly todayUtc) => new()
    {
        OwnerId = ownerId,
        CohortStartedOnUtc = todayUtc,
        LastActiveOnUtc = todayUtc,
        ReturnedInDays30To59 = false,
        RetentionExpiresAtUtc = ProductActivityCohortRules.RetentionExpiresAtUtc(todayUtc),
    };
}
