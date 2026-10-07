using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Relio.Application.Metrics;
using Relio.Application.Security;
using Relio.Data.Concurrency;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Metrics;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Isolation;

[Collection(SqlServerCollection.Name)]
public sealed class ProductActivityLaneSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Concurrent_activity_records_are_serialized_on_one_context()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        await using var dbContext = fixture.CreateDbContext(
            new SlowReaderInterceptor(TimeSpan.FromMilliseconds(150)));
        var implementation = new ProductActivityService(
            dbContext,
            new FakeCurrentUser(harness.OwnerA.Id),
            new TestOptionsMonitor(new ProductMetricsOptions { Enabled = true }),
            new ProductMetricsCollectionGate(),
            harness.Clock);
        var service = DatabaseLaneProxy<IProductActivityService>.Create(implementation, dbContext.Lane);

        var act = async () => await Task.WhenAll(service.RecordAsync(), service.RecordAsync());

        await act.Should().NotThrowAsync();
        await using var verify = fixture.CreateDbContext();
        (await verify.Set<ProductActivity>().AsNoTracking()
            .CountAsync(activity => activity.OwnerId == harness.OwnerA.Id))
            .Should().Be(1);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    private sealed class TestOptionsMonitor(ProductMetricsOptions value) : IOptionsMonitor<ProductMetricsOptions>
    {
        public ProductMetricsOptions CurrentValue => value;

        public ProductMetricsOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<ProductMetricsOptions, string?> listener) => null;
    }
}
