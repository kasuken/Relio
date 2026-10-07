using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Relio.Application.Administration;
using Relio.Application.Metrics;
using Relio.Application.Security;
using Relio.Data.Metrics;
using Relio.Data.IntegrationTests.Infrastructure;

namespace Relio.Data.IntegrationTests.Isolation;

[Collection(SqlServerCollection.Name)]
public sealed class ProductMetricsPrivilegeSqlIsolationTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Report_requires_an_active_administrator_and_rejects_anonymous_or_disabled_callers()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture, makeOwnerAAdministrator: true);
        await using (var disableContext = fixture.CreateDbContext())
        {
            var administrator = await disableContext.Users
                .SingleAsync(user => user.Id == harness.OwnerA.Id);
            administrator.IsDisabled = true;
            await disableContext.SaveChangesAsync();
        }

        await using var context = fixture.CreateDbContext();
        var options = new TestOptionsMonitor(new ProductMetricsOptions { Enabled = true });
        var disabledAdministrator = new ProductMetricsReportService(
            context,
            new FakeCurrentUser(harness.OwnerA.Id),
            options,
            harness.Clock);
        var disabledCall = () => disabledAdministrator.GetAsync();
        await disabledCall.Should().ThrowAsync<AdministratorRequiredException>();

        var anonymousReport = new ProductMetricsReportService(
            context,
            new FakeCurrentUser(null),
            options,
            harness.Clock);
        var anonymousCall = () => anonymousReport.GetAsync();
        await anonymousCall.Should().ThrowAsync<UnauthenticatedUserException>();

        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    private sealed class TestOptionsMonitor(ProductMetricsOptions value) : IOptionsMonitor<ProductMetricsOptions>
    {
        public ProductMetricsOptions CurrentValue => value;

        public ProductMetricsOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<ProductMetricsOptions, string?> listener) => null;
    }
}
