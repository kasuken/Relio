using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Relio.Application.Metrics;
using Relio.Application.Security;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Metrics;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Isolation;

[Collection(SqlServerCollection.Name)]
public sealed class ProductActivitySqlIsolationTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task RecordAsync_uses_the_authenticated_active_account_and_is_idempotent_per_day()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        var disabledOwner = await harness.CreateAdditionalAccountAsync();
        await using (var setup = fixture.CreateDbContext())
        {
            var disabledAccount = await setup.Users.SingleAsync(user => user.Id == disabledOwner.Id);
            disabledAccount.IsDisabled = true;
            await setup.SaveChangesAsync();
        }

        await using (var anonymousScope = harness.As(null))
        {
            var anonymousService = CreateService(anonymousScope.DbContext, anonymousScope.CurrentUser, harness.Clock, enabled: true);
            var act = () => anonymousService.RecordAsync();
            await act.Should().ThrowAsync<UnauthenticatedUserException>();
            anonymousScope.DbContext.ChangeTracker.Entries().Should().BeEmpty();
        }

        await using (var ownerAScope = harness.AsOwnerA())
        {
            var activityA = CreateService(ownerAScope.DbContext, ownerAScope.CurrentUser, harness.Clock, enabled: true);
            await activityA.RecordAsync();
            await activityA.RecordAsync();
        }

        await using (var ownerBScope = harness.AsOwnerB())
        {
            var activityB = CreateService(ownerBScope.DbContext, ownerBScope.CurrentUser, harness.Clock, enabled: true);
            await activityB.RecordAsync();
        }

        await using (var disabledOwnerScope = harness.As(disabledOwner.Id))
        {
            var disabledActivity = CreateService(
                disabledOwnerScope.DbContext,
                disabledOwnerScope.CurrentUser,
                harness.Clock,
                enabled: true);
            await disabledActivity.RecordAsync();
        }

        await using var verify = fixture.CreateDbContext();
        var activityRows = await verify.Set<ProductActivity>().AsNoTracking()
            .Where(activity => activity.OwnerId == harness.OwnerA.Id
                || activity.OwnerId == harness.OwnerB.Id
                || activity.OwnerId == disabledOwner.Id)
            .ToListAsync();
        activityRows.Should().ContainSingle(activity => activity.OwnerId == harness.OwnerA.Id);
        activityRows.Should().ContainSingle(activity => activity.OwnerId == harness.OwnerB.Id);
        activityRows.Should().NotContain(activity => activity.OwnerId == disabledOwner.Id);
        var persistedActivityA = activityRows.Single(activity => activity.OwnerId == harness.OwnerA.Id);
        var persistedActivityB = activityRows.Single(activity => activity.OwnerId == harness.OwnerB.Id);
        persistedActivityA.CohortStartedOnUtc.Should()
            .Be(DateOnly.FromDateTime(harness.Clock.GetUtcNow().UtcDateTime));
        persistedActivityA.LastActiveOnUtc.Should().Be(persistedActivityA.CohortStartedOnUtc);
        persistedActivityA.ReturnedInDays30To59.Should().BeFalse();
        persistedActivityB.CohortStartedOnUtc.Should().Be(persistedActivityA.CohortStartedOnUtc);
        persistedActivityB.LastActiveOnUtc.Should().Be(persistedActivityB.CohortStartedOnUtc);
    }

    [SqlServerFact]
    public async Task RecordAsync_when_collection_is_disabled_and_anonymous_still_requires_authentication()
    {
        var capture = new CommandCountInterceptor();
        await using var interceptedContext = fixture.CreateDbContext(capture);
        var anonymous = new FakeCurrentUser(null);
        var disabledService = CreateService(interceptedContext, anonymous, TimeProvider.System, enabled: false);

        var act = () => disabledService.RecordAsync();

        await act.Should().ThrowAsync<UnauthenticatedUserException>();
        capture.ReaderCommands.Should().Be(0);
        interceptedContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [SqlServerFact]
    public async Task RecordAsync_when_collection_is_disabled_and_authenticated_does_not_query_SQL()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        var capture = new CommandCountInterceptor();
        await using var interceptedContext = fixture.CreateDbContext(capture);
        var currentUser = new FakeCurrentUser(harness.OwnerA.Id);
        var disabledService = CreateService(interceptedContext, currentUser, TimeProvider.System, enabled: false);

        await disabledService.RecordAsync();

        capture.ReaderCommands.Should().Be(0);
        interceptedContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    private static ProductActivityService CreateService(
        RelioDbContext dbContext,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        bool enabled) =>
        new(
            dbContext,
            currentUser,
            new TestOptionsMonitor(new ProductMetricsOptions { Enabled = enabled }),
            new ProductMetricsCollectionGate(),
            timeProvider);

    private sealed class TestOptionsMonitor(ProductMetricsOptions value) : IOptionsMonitor<ProductMetricsOptions>
    {
        public ProductMetricsOptions CurrentValue => value;

        public ProductMetricsOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<ProductMetricsOptions, string?> listener) => null;
    }

    private sealed class CommandCountInterceptor : DbCommandInterceptor
    {
        private int _readerCommands;

        public int ReaderCommands => Volatile.Read(ref _readerCommands);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _readerCommands);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
