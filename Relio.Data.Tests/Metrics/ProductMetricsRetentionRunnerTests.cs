using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Metrics;
using Relio.Data.Metrics;
using Relio.Domain;

namespace Relio.Data.Tests.Metrics;

public sealed class ProductMetricsRetentionRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_when_enabled_removes_expired_contributions_only()
    {
        var timeProvider = new FakeTimeProvider(Now);
        await using var dbContext = MetricsTestSupport.CreateDbContext(Guid.NewGuid().ToString(), timeProvider);
        var expired = Activity("expired", new DateOnly(2026, 2, 28));
        var retained = Activity("retained", new DateOnly(2026, 5, 31));
        var person = new Person
        {
            OwnerId = "owner",
            FirstName = "Private",
            Details = "This content must not be loaded or changed by metrics cleanup.",
        };
        dbContext.Set<ProductActivity>().AddRange(expired, retained);
        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var runner = new ProductMetricsRetentionRunner(
            dbContext,
            MetricsTestSupport.Options(enabled: true),
            new ProductMetricsCollectionGate(),
            timeProvider);

        var removedCount = await runner.RunAsync();

        removedCount.Should().Be(1);
        (await dbContext.Set<ProductActivity>().AsNoTracking().SingleAsync()).OwnerId.Should().Be("retained");
        (await dbContext.People.AsNoTracking().SingleAsync()).Details
            .Should().Be("This content must not be loaded or changed by metrics cleanup.");
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_when_disabled_purges_every_contribution_but_no_other_data()
    {
        var timeProvider = new FakeTimeProvider(Now);
        await using var dbContext = MetricsTestSupport.CreateDbContext(Guid.NewGuid().ToString(), timeProvider);
        dbContext.Set<ProductActivity>().AddRange(
            Activity("expired", new DateOnly(2026, 2, 28)),
            Activity("still-retained", new DateOnly(2026, 5, 31)));
        var person = new Person { OwnerId = "owner", FirstName = "Private" };
        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var runner = new ProductMetricsRetentionRunner(
            dbContext,
            MetricsTestSupport.Options(enabled: false),
            new ProductMetricsCollectionGate(),
            timeProvider);

        var removedCount = await runner.RunAsync();

        removedCount.Should().Be(2);
        (await dbContext.Set<ProductActivity>().CountAsync()).Should().Be(0);
        (await dbContext.People.CountAsync()).Should().Be(1);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_deletes_more_than_one_bounded_batch_in_separate_saves()
    {
        var timeProvider = new FakeTimeProvider(Now);
        var databaseName = Guid.NewGuid().ToString();
        await using (var seedContext = MetricsTestSupport.CreateDbContext(databaseName, timeProvider))
        {
            seedContext.Set<ProductActivity>().AddRange(
                Enumerable.Range(0, 501)
                    .Select(index => Activity($"owner-{index}", new DateOnly(2026, 1, 1))));
            await seedContext.SaveChangesAsync();
        }

        var saves = new MetricsTestSupport.SaveCountingInterceptor();
        await using var dbContext = MetricsTestSupport.CreateDbContext(databaseName, timeProvider, saves);
        var runner = new ProductMetricsRetentionRunner(
            dbContext,
            MetricsTestSupport.Options(enabled: false),
            new ProductMetricsCollectionGate(),
            timeProvider);

        var removedCount = await runner.RunAsync();

        removedCount.Should().Be(501);
        saves.SaveCount.Should().Be(2, "the cleanup removes no more than 500 tracked rows per save");
        (await dbContext.Set<ProductActivity>().CountAsync()).Should().Be(0);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    private static ProductActivity Activity(string ownerId, DateOnly startedOn) => new()
    {
        OwnerId = ownerId,
        CohortStartedOnUtc = startedOn,
        LastActiveOnUtc = startedOn,
        ReturnedInDays30To59 = false,
        RetentionExpiresAtUtc = ProductActivityCohortRules.RetentionExpiresAtUtc(startedOn),
    };
}
