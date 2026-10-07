using Relio.Application.Portability;
using Relio.Application.Security;
using Relio.Data.Concurrency;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Portability;

namespace Relio.Data.IntegrationTests.Isolation;

[Collection(SqlServerCollection.Name)]
public sealed class UserDataPortabilityLaneSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Concurrent_exports_are_serialized_on_one_context()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        await UserDataPortabilitySqlIsolationTests.SeedOwnerGraphAsync(
            fixture,
            harness,
            harness.OwnerA.Id,
            "A");
        await using var dbContext = fixture.CreateDbContext(
            new SlowReaderInterceptor(TimeSpan.FromMilliseconds(150)));
        var implementation = new UserDataPortabilityService(
            dbContext,
            new FakeCurrentUser(harness.OwnerA.Id),
            harness.Clock);
        var service = DatabaseLaneProxy<IUserDataPortabilityService>.Create(
            implementation,
            dbContext.Lane);

        var exportTask = service.ExportAsync();
        var vCardTask = service.ExportPeopleVCardAsync();

        await Task.WhenAll(exportTask, vCardTask);
        exportTask.Result.People.Should().HaveCount(2);
        vCardTask.Result.Should().Contain("AdaA");
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }
}
