using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Relio.Application.Administration;
using Relio.Application.Metrics;
using Relio.Application.Security;
using Relio.Data.Identity;
using Relio.Domain;

namespace Relio.Data.Metrics;

/// <summary>
/// Reads aggregate product metrics for an active Administrator. Every data query is a
/// SQL-translatable count or aggregate projection; private content is never materialized.
/// </summary>
public sealed class ProductMetricsReportService(
    RelioDbContext dbContext,
    ICurrentUser currentUser,
    IOptionsMonitor<ProductMetricsOptions> options,
    TimeProvider timeProvider) : IProductMetricsReportService
{
    private readonly RelioDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly ICurrentUser _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    private readonly IOptionsMonitor<ProductMetricsOptions> _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public async Task<ProductMetricsReport> GetAsync(CancellationToken cancellationToken = default)
    {
        var callerId = _currentUser.RequireUserId();
        if (!await IsActiveAdministratorAsync(callerId, cancellationToken))
        {
            throw new AdministratorRequiredException();
        }

        if (!_options.CurrentValue.Enabled)
        {
            return ProductMetricsReport.Disabled;
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var todayUtc = DateOnly.FromDateTime(utcNow);
        var monthStartUtc = new DateOnly(todayUtc.Year, todayUtc.Month, 1);
        var nextMonthStartUtc = monthStartUtc.AddMonths(1);

        var liveAccountCount = await _dbContext.Users
            .AsNoTracking()
            .LongCountAsync(cancellationToken);

        var peopleCount = await _dbContext.Set<Person>()
            .AsNoTracking()
            .LongCountAsync(cancellationToken);

        var interactionsThisUtcMonth = await _dbContext.Interactions
            .AsNoTracking()
            .LongCountAsync(
                interaction => interaction.OccurredOn >= monthStartUtc
                    && interaction.OccurredOn < nextMonthStartUtc,
                cancellationToken);

        var savedReminderCount = await _dbContext.Reminders
            .AsNoTracking()
            .LongCountAsync(cancellationToken);

        var accountsWithSavedReminders = await _dbContext.Users
            .AsNoTracking()
            .Where(user => _dbContext.Reminders.Any(reminder => reminder.OwnerId == user.Id))
            .LongCountAsync(cancellationToken);

        var oldestCompletedCohort = todayUtc.AddDays(-ProductActivityCohortRules.CompletedCohortEndDay);
        var newestCompletedCohort = todayUtc.AddDays(-ProductActivityCohortRules.CompletedCohortStartDay);
        var eligibleActivities = _dbContext.Set<ProductActivity>()
            .AsNoTracking()
            .Where(activity =>
                activity.CohortStartedOnUtc >= oldestCompletedCohort
                && activity.CohortStartedOnUtc <= newestCompletedCohort
                && activity.RetentionExpiresAtUtc > utcNow);

        var eligibleCohorts = await eligibleActivities.LongCountAsync(cancellationToken);
        var returnedCohorts = eligibleCohorts == 0
            ? 0
            : await eligibleActivities
                .Where(activity => activity.ReturnedInDays30To59)
                .LongCountAsync(cancellationToken);

        return new ProductMetricsReport(
            IsEnabled: true,
            LiveAccountCount: liveAccountCount,
            OwnedPeopleCount: peopleCount,
            AveragePeoplePerAccount: Average(peopleCount, liveAccountCount),
            CurrentUtcMonthStart: monthStartUtc,
            InteractionsThisUtcMonth: interactionsThisUtcMonth,
            SavedReminderCount: savedReminderCount,
            AccountsWithSavedReminders: accountsWithSavedReminders,
            AccountsWithSavedRemindersPercent: Ratio(accountsWithSavedReminders, liveAccountCount),
            EligibleRetentionCohorts: eligibleCohorts,
            ReturnedRetentionCohorts: returnedCohorts,
            RetentionRatePercent: Ratio(returnedCohorts, eligibleCohorts));
    }

    private Task<bool> IsActiveAdministratorAsync(string userId, CancellationToken cancellationToken) =>
        (from user in _dbContext.Users.AsNoTracking()
         join userRole in _dbContext.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
         join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
         where user.Id == userId
             && !user.IsDisabled
             && role.Name == RelioRoles.Administrator
         select user.Id)
        .AnyAsync(cancellationToken);

    private static decimal? Ratio(long numerator, long denominator) =>
        denominator == 0
            ? null
            : decimal.Round((decimal)numerator * 100m / denominator, 1, MidpointRounding.AwayFromZero);

    private static decimal? Average(long total, long denominator) =>
        denominator == 0
            ? null
            : decimal.Round((decimal)total / denominator, 1, MidpointRounding.AwayFromZero);
}
