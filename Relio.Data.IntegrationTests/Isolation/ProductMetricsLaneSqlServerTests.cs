using Microsoft.Extensions.Options;
using Relio.Application.Metrics;
using Relio.Application.Security;
using Relio.Data.Concurrency;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Metrics;

namespace Relio.Data.IntegrationTests.Isolation;

[Collection(SqlServerCollection.Name)]
public sealed class ProductMetricsLaneSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Concurrent_aggregate_reports_are_serialized_on_one_context()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture, makeOwnerAAdministrator: true);
        await using var dbContext = fixture.CreateDbContext(
            new SlowReaderInterceptor(TimeSpan.FromMilliseconds(150)));
        var implementation = new ProductMetricsReportService(
            dbContext,
            new FakeCurrentUser(harness.OwnerA.Id),
            new TestOptionsMonitor(new ProductMetricsOptions { Enabled = true }),
            harness.Clock);
        var service = DatabaseLaneProxy<IProductMetricsReportService>.Create(
            implementation,
            dbContext.Lane);

        var firstReport = service.GetAsync();
        var secondReport = service.GetAsync();

        await Task.WhenAll(firstReport, secondReport);
        firstReport.Result.IsEnabled.Should().BeTrue();
        secondReport.Result.IsEnabled.Should().BeTrue();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    private sealed class TestOptionsMonitor(ProductMetricsOptions value) : IOptionsMonitor<ProductMetricsOptions>
    {
        public ProductMetricsOptions CurrentValue => value;

        public ProductMetricsOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<ProductMetricsOptions, string?> listener) => null;
    }
}
