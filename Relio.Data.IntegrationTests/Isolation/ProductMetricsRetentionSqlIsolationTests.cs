using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Relio.Application.Metrics;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Metrics;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Isolation;

[Collection(SqlServerCollection.Name)]
public sealed class ProductMetricsRetentionSqlIsolationTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Trusted_retention_cleans_expired_rows_for_identity_owners_without_removing_retained_rows()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        var today = ProductActivityCohortRules.UtcToday(TimeProvider.System);
        var fixedNow = new DateTimeOffset(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        Guid retainedActivityId;
        await using (var seedContext = fixture.CreateDbContext())
        {
            var expired = await seedContext.Set<ProductActivity>()
                .SingleOrDefaultAsync(activity => activity.OwnerId == harness.OwnerA.Id);
            expired ??= CreateActivity(harness.OwnerA.Id, today.AddDays(-90));
            if (seedContext.Entry(expired).State == EntityState.Detached)
            {
                seedContext.Set<ProductActivity>().Add(expired);
            }

            var retained = await seedContext.Set<ProductActivity>()
                .SingleOrDefaultAsync(activity => activity.OwnerId == harness.OwnerB.Id);
            retained ??= CreateActivity(harness.OwnerB.Id, today.AddDays(-89));
            if (seedContext.Entry(retained).State == EntityState.Detached)
            {
                seedContext.Set<ProductActivity>().Add(retained);
            }

            SetActivity(expired, today.AddDays(-90));
            SetActivity(retained, today.AddDays(-89));
            retainedActivityId = retained.Id;
            await seedContext.SaveChangesAsync();
        }

        await using var dbContext = fixture.CreateDbContext();
        var runner = new ProductMetricsRetentionRunner(
            dbContext,
            new TestOptionsMonitor(new ProductMetricsOptions { Enabled = true }),
            new ProductMetricsCollectionGate(),
            new FixedTimeProvider(fixedNow));

        var removedCount = await runner.RunAsync();

        removedCount.Should().BeGreaterThanOrEqualTo(1);
        var expiredActivityRemains = await dbContext.Set<ProductActivity>().AsNoTracking()
            .AnyAsync(activity => activity.OwnerId == harness.OwnerA.Id);
        expiredActivityRemains.Should().BeFalse();
        var retainedActivity = await dbContext.Set<ProductActivity>().AsNoTracking()
            .SingleAsync(activity => activity.OwnerId == harness.OwnerB.Id);
        retainedActivity.Id.Should().Be(retainedActivityId);
        retainedActivity.CohortStartedOnUtc.Should().Be(today.AddDays(-89));
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    private static void SetActivity(ProductActivity activity, DateOnly startedOn)
    {
        activity.CohortStartedOnUtc = startedOn;
        activity.LastActiveOnUtc = startedOn;
        activity.ReturnedInDays30To59 = false;
        activity.RetentionExpiresAtUtc = ProductActivityCohortRules.RetentionExpiresAtUtc(startedOn);
    }

    private static ProductActivity CreateActivity(string ownerId, DateOnly startedOn) => new()
    {
        OwnerId = ownerId,
        CohortStartedOnUtc = startedOn,
        LastActiveOnUtc = startedOn,
        ReturnedInDays30To59 = false,
        RetentionExpiresAtUtc = ProductActivityCohortRules.RetentionExpiresAtUtc(startedOn),
    };

    private sealed class TestOptionsMonitor(ProductMetricsOptions value) : IOptionsMonitor<ProductMetricsOptions>
    {
        public ProductMetricsOptions CurrentValue => value;

        public ProductMetricsOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<ProductMetricsOptions, string?> listener) => null;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
