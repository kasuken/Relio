using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Administration;
using Relio.Application.Metrics;
using Relio.Application.Security;
using Relio.Data.Metrics;
using Relio.Data.Tests.People;
using Relio.Domain;

namespace Relio.Data.Tests.Metrics;

public sealed class ProductMetricsReportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 31, 23, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_includes_disabled_accounts_and_their_data_in_consistent_denominators()
    {
        var timeProvider = new FakeTimeProvider(Now);
        await using var dbContext = MetricsTestSupport.CreateDbContext(Guid.NewGuid().ToString(), timeProvider);
        var administrator = await MetricsTestSupport.CreateUserAsync(
            dbContext,
            "metrics-admin",
            administrator: true);
        var member = await MetricsTestSupport.CreateUserAsync(dbContext, "metrics-member");
        var disabled = await MetricsTestSupport.CreateUserAsync(dbContext, "metrics-disabled", disabled: true);
        var adminPerson = new Person { OwnerId = administrator.Id, FirstName = "Private admin name" };
        var archivedPerson = new Person { OwnerId = member.Id, FirstName = "Private archived name", IsArchived = true };
        var disabledPerson = new Person { OwnerId = disabled.Id, FirstName = "Private disabled name" };
        dbContext.People.AddRange(adminPerson, archivedPerson, disabledPerson);

        dbContext.Interactions.AddRange(
            new Interaction
            {
                OwnerId = member.Id,
                OccurredOn = new DateOnly(2026, 3, 1),
                Description = "Private interaction text",
            },
            new Interaction
            {
                OwnerId = disabled.Id,
                OccurredOn = new DateOnly(2026, 3, 31),
                Description = "Private disabled interaction",
            },
            new Interaction
            {
                OwnerId = member.Id,
                OccurredOn = new DateOnly(2026, 4, 1),
                Description = "Outside this UTC month",
            });

        dbContext.Reminders.AddRange(
            new Reminder
            {
                OwnerId = member.Id,
                Person = archivedPerson,
                Title = "Private completed reminder",
                DueDate = new DateOnly(2026, 3, 1),
                IsCompleted = true,
            },
            new Reminder
            {
                OwnerId = disabled.Id,
                Person = disabledPerson,
                Title = "Private disabled reminder",
                DueDate = new DateOnly(2026, 3, 2),
            });

        var eligibleReturned = Activity("retained-cohort", new DateOnly(2026, 1, 30), returned: true);
        var eligibleNotReturned = Activity("not-retained-cohort", new DateOnly(2026, 1, 1), returned: false);
        var tooNew = Activity("incomplete-cohort", new DateOnly(2026, 1, 31), returned: true);
        var expired = Activity("expired-cohort", new DateOnly(2025, 12, 31), returned: true);
        dbContext.Set<ProductActivity>().AddRange(eligibleReturned, eligibleNotReturned, tooNew, expired);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = CreateService(dbContext, administrator.Id, enabled: true, timeProvider);

        var report = await service.GetAsync();

        report.IsEnabled.Should().BeTrue();
        report.LiveAccountCount.Should().Be(3, "the account total includes every existing account row");
        report.OwnedPeopleCount.Should().Be(3, "the total includes archived profiles and existing disabled-account data");
        report.AveragePeoplePerAccount.Should().Be(1m);
        report.CurrentUtcMonthStart.Should().Be(new DateOnly(2026, 3, 1));
        report.InteractionsThisUtcMonth.Should().Be(2, "OccurredOn uses the UTC calendar month and is not converted to an instant");
        report.SavedReminderCount.Should().Be(2, "all saved reminders count, including completed and disabled-account rows");
        report.AccountsWithSavedReminders.Should().Be(2, "the numerator includes the disabled account with a reminder");
        report.AccountsWithSavedRemindersPercent.Should().Be(66.7m);
        report.EligibleRetentionCohorts.Should().Be(2);
        report.ReturnedRetentionCohorts.Should().Be(1);
        report.RetentionRatePercent.Should().Be(50m);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_returns_zero_counts_and_no_retention_rate_without_product_activity()
    {
        var timeProvider = new FakeTimeProvider(Now);
        await using var dbContext = MetricsTestSupport.CreateDbContext(Guid.NewGuid().ToString(), timeProvider);
        var administrator = await MetricsTestSupport.CreateUserAsync(
            dbContext,
            "empty-metrics-admin",
            administrator: true);

        var report = await CreateService(dbContext, administrator.Id, enabled: true, timeProvider).GetAsync();

        report.LiveAccountCount.Should().Be(1, "the active administrator is included in the live-account denominator");
        report.OwnedPeopleCount.Should().Be(0);
        report.AveragePeoplePerAccount.Should().Be(0m);
        report.InteractionsThisUtcMonth.Should().Be(0);
        report.SavedReminderCount.Should().Be(0);
        report.AccountsWithSavedReminders.Should().Be(0);
        report.AccountsWithSavedRemindersPercent.Should().Be(0m);
        report.EligibleRetentionCohorts.Should().Be(0);
        report.RetentionRatePercent.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_when_disabled_returns_static_status_without_running_metric_queries()
    {
        var queries = new MetricsTestSupport.QueryCountingInterceptor();
        var timeProvider = new FakeTimeProvider(Now);
        await using var dbContext = MetricsTestSupport.CreateDbContext(Guid.NewGuid().ToString(), timeProvider, queries);
        var administrator = await MetricsTestSupport.CreateUserAsync(
            dbContext,
            "disabled-report-admin",
            administrator: true);
        queries.Reset();

        var report = await CreateService(dbContext, administrator.Id, enabled: false, timeProvider).GetAsync();

        report.Should().BeSameAs(ProductMetricsReport.Disabled);
        queries.QueryCount.Should().Be(1, "only the active administrator check is necessary");
    }

    [Fact]
    public async Task GetAsync_rejects_anonymous_and_nonadministrator_callers()
    {
        var queries = new MetricsTestSupport.QueryCountingInterceptor();
        var timeProvider = new FakeTimeProvider(Now);
        await using var dbContext = MetricsTestSupport.CreateDbContext(Guid.NewGuid().ToString(), timeProvider, queries);
        var member = await MetricsTestSupport.CreateUserAsync(dbContext, "metrics-report-member");

        var anonymousAct = () => CreateService(dbContext, null, enabled: true, timeProvider).GetAsync();
        await anonymousAct.Should().ThrowAsync<UnauthenticatedUserException>();
        queries.QueryCount.Should().Be(0);

        var memberAct = () => CreateService(dbContext, member.Id, enabled: true, timeProvider).GetAsync();
        await memberAct.Should().ThrowAsync<AdministratorRequiredException>();
        queries.QueryCount.Should().Be(1);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_rechecks_disabled_status_and_role_from_the_database_not_a_tracked_user()
    {
        var timeProvider = new FakeTimeProvider(Now);
        var databaseName = Guid.NewGuid().ToString();
        await using var reportContext = MetricsTestSupport.CreateDbContext(databaseName, timeProvider);
        var administrator = await MetricsTestSupport.CreateUserAsync(
            reportContext,
            "stale-admin",
            administrator: true);
        _ = await reportContext.Users.SingleAsync(user => user.Id == administrator.Id);

        await using (var externalContext = MetricsTestSupport.CreateDbContext(databaseName, timeProvider))
        {
            var user = await externalContext.Users.SingleAsync(candidate => candidate.Id == administrator.Id);
            user.IsDisabled = true;
            await externalContext.SaveChangesAsync();
        }

        var disabledService = CreateService(reportContext, administrator.Id, enabled: true, timeProvider);
        var disabledAct = () => disabledService.GetAsync();
        await disabledAct.Should().ThrowAsync<AdministratorRequiredException>();

        await using (var externalContext = MetricsTestSupport.CreateDbContext(databaseName, timeProvider))
        {
            var user = await externalContext.Users.SingleAsync(candidate => candidate.Id == administrator.Id);
            user.IsDisabled = false;
            var role = await externalContext.Roles.AsNoTracking()
                .SingleAsync(existing => existing.Name == RelioRoles.Administrator);
            var userRole = await externalContext.UserRoles.SingleAsync(candidate =>
                candidate.UserId == administrator.Id && candidate.RoleId == role.Id);
            externalContext.UserRoles.Remove(userRole);
            await externalContext.SaveChangesAsync();
        }

        var demotedAct = () => CreateService(reportContext, administrator.Id, enabled: true, timeProvider).GetAsync();
        await demotedAct.Should().ThrowAsync<AdministratorRequiredException>();
        reportContext.ChangeTracker.Clear();
    }

    private static ProductActivity Activity(string ownerId, DateOnly startedOn, bool returned) => new()
    {
        OwnerId = ownerId,
        CohortStartedOnUtc = startedOn,
        LastActiveOnUtc = returned ? startedOn.AddDays(30) : startedOn,
        ReturnedInDays30To59 = returned,
        RetentionExpiresAtUtc = ProductActivityCohortRules.RetentionExpiresAtUtc(startedOn),
    };

    private static ProductMetricsReportService CreateService(
        RelioDbContext dbContext,
        string? currentUserId,
        bool enabled,
        TimeProvider timeProvider) =>
        new(dbContext, new FakeCurrentUser(currentUserId), MetricsTestSupport.Options(enabled), timeProvider);
}
