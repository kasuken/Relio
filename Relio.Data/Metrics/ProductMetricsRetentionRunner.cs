using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Relio.Application.Metrics;
using Relio.Domain;

namespace Relio.Data.Metrics;

/// <summary>
/// Trusted background-only expiry worker for product activity contributions. It only loads,
/// deletes, and counts <see cref="ProductActivity"/> rows.
/// </summary>
public sealed class ProductMetricsRetentionRunner(
    RelioDbContext dbContext,
    IOptionsMonitor<ProductMetricsOptions> options,
    IProductMetricsCollectionGate collectionGate,
    TimeProvider timeProvider) : IProductMetricsRetentionRunner
{
    private const int BatchSize = 500;

    private readonly RelioDbContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly IOptionsMonitor<ProductMetricsOptions> _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IProductMetricsCollectionGate _collectionGate =
        collectionGate ?? throw new ArgumentNullException(nameof(collectionGate));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public async Task<long> RunAsync(CancellationToken cancellationToken = default)
    {
        using var collectionLease = await _collectionGate.EnterAsync(cancellationToken);
        var removedCount = 0L;
        var metricsEnabled = _options.CurrentValue.Enabled;
        try
        {
            while (true)
            {
                var activities = _dbContext.Set<ProductActivity>();
                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                var batch = metricsEnabled
                    ? await activities
                        .Where(activity => activity.RetentionExpiresAtUtc <= nowUtc)
                        .OrderBy(activity => activity.RetentionExpiresAtUtc)
                        .Take(BatchSize)
                        .ToListAsync(cancellationToken)
                    : await activities
                        .OrderBy(activity => activity.Id)
                        .Take(BatchSize)
                        .ToListAsync(cancellationToken);

                if (batch.Count == 0)
                {
                    return removedCount;
                }

                activities.RemoveRange(batch);
                await _dbContext.SaveChangesAsync(cancellationToken);
                removedCount += batch.Count;
                _dbContext.ChangeTracker.Clear();
            }
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }
}
