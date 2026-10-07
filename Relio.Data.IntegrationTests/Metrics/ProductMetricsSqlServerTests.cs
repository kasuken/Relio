using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Relio.Application.Administration;
using Relio.Application.Metrics;
using Relio.Data.Identity;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Metrics;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Metrics;

[Collection(SqlServerCollection.Name)]
public sealed class ProductMetricsSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Report_uses_SQL_translatable_aggregates_without_selecting_narrative_columns()
    {
        var todayUtc = ProductActivityCohortRules.UtcToday(TimeProvider.System);
        var monthStartUtc = new DateOnly(todayUtc.Year, todayUtc.Month, 1);
        var nextMonthStartUtc = monthStartUtc.AddMonths(1);
        var administrator = await CreateUserAsync(administrator: true);
        var member = await CreateUserAsync(administrator: false);
        var start = todayUtc.AddDays(-60);
        var person = new Person
        {
            OwnerId = member.Id,
            FirstName = "Private report test name",
            Details = "Private report test details",
            IsArchived = true,
        };
        var activity = NewActivity(member.Id, start, returned: true);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.People.Add(person);
            seedContext.Interactions.Add(new Interaction
            {
                OwnerId = member.Id,
                OccurredOn = monthStartUtc,
                Description = "Private SQL translation text",
            });
            seedContext.Interactions.Add(new Interaction
            {
                OwnerId = member.Id,
                OccurredOn = nextMonthStartUtc,
                Description = "Outside the report month",
            });
            seedContext.Reminders.Add(new Reminder
            {
                OwnerId = member.Id,
                Person = person,
                Title = "Private SQL reminder",
                DueDate = monthStartUtc,
                IsCompleted = true,
            });
            seedContext.Set<ProductActivity>().Add(activity);
            await seedContext.SaveChangesAsync();
        }

        var sqlCapture = new SqlCaptureInterceptor();
        await using var reportContext = fixture.CreateDbContext(sqlCapture);
        var report = await CreateReportService(reportContext, administrator.Id, TimeProvider.System).GetAsync();

        report.IsEnabled.Should().BeTrue();
        report.LiveAccountCount.Should().BeGreaterThanOrEqualTo(2);
        report.OwnedPeopleCount.Should().BeGreaterThanOrEqualTo(1);
        report.InteractionsThisUtcMonth.Should().BeGreaterThanOrEqualTo(1);
        report.SavedReminderCount.Should().BeGreaterThanOrEqualTo(1);
        report.AccountsWithSavedReminders.Should().BeGreaterThanOrEqualTo(1);
        report.EligibleRetentionCohorts.Should().BeGreaterThanOrEqualTo(1);
        report.ReturnedRetentionCohorts.Should().BeGreaterThanOrEqualTo(1);
        sqlCapture.Commands.Should().NotBeEmpty();
        sqlCapture.Commands.Should().OnlyContain(command =>
            !command.Contains("[Description]", StringComparison.OrdinalIgnoreCase)
            && !command.Contains("[Details]", StringComparison.OrdinalIgnoreCase)
            && !command.Contains("[Title]", StringComparison.OrdinalIgnoreCase)
            && !command.Contains("[FirstName]", StringComparison.OrdinalIgnoreCase)
            && !command.Contains("[LastName]", StringComparison.OrdinalIgnoreCase));
    }

    [SqlServerFact]
    public async Task RecordAsync_recovers_only_the_owner_unique_index_create_race()
    {
        var user = await CreateUserAsync(administrator: false);
        var now = TimeProvider.System.GetUtcNow();
        var competingInsert = new InsertProductActivityOnFirstSaveInterceptor(
            fixture,
            user.Id,
            DateOnly.FromDateTime(now.UtcDateTime));
        await using var dbContext = fixture.CreateDbContext(competingInsert);
        var service = new ProductActivityService(
            dbContext,
            new FakeCurrentUser(user.Id),
            Options(enabled: true),
            new ProductMetricsCollectionGate(),
            TimeProvider.System);

        await service.RecordAsync();

        await using var verifyContext = fixture.CreateDbContext();
        var activities = await verifyContext.Set<ProductActivity>()
            .AsNoTracking()
            .Where(activity => activity.OwnerId == user.Id)
            .ToListAsync();
        activities.Should().ContainSingle();
        activities[0].CohortStartedOnUtc.Should().Be(DateOnly.FromDateTime(now.UtcDateTime));
        activities[0].ReturnedInDays30To59.Should().BeFalse();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [SqlServerFact]
    public async Task RecordAsync_does_not_treat_a_primary_key_collision_as_an_owner_race()
    {
        var user = await CreateUserAsync(administrator: false);
        var now = TimeProvider.System.GetUtcNow();
        var idCollision = new InsertProductActivityIdCollisionOnFirstSaveInterceptor(
            fixture,
            await TestDataFactory.CreateOwnerAsync(fixture),
            DateOnly.FromDateTime(now.UtcDateTime));
        await using var dbContext = fixture.CreateDbContext(idCollision);
        var service = new ProductActivityService(
            dbContext,
            new FakeCurrentUser(user.Id),
            Options(enabled: true),
            new ProductMetricsCollectionGate(),
            TimeProvider.System);

        var exception = await FluentActions.Awaiting(() => service.RecordAsync())
            .Should().ThrowAsync<ProductActivityWriteException>();

        exception.Which.InnerException.Should().BeNull();
        exception.Which.Message.Should().NotContain(user.Id);
        await using var verifyContext = fixture.CreateDbContext();
        (await verifyContext.Set<ProductActivity>().AsNoTracking()
            .AnyAsync(activity => activity.OwnerId == user.Id))
            .Should().BeFalse();
        (await verifyContext.Set<ProductActivity>().AsNoTracking()
            .AnyAsync(activity => activity.OwnerId == idCollision.OtherOwnerId))
            .Should().BeTrue("the separate primary-key collision row was committed, but the failed write was not swallowed");
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [SqlServerFact]
    public async Task Report_rejects_a_real_nonadministrator_even_if_the_page_is_reached_directly()
    {
        var member = await CreateUserAsync(administrator: false);
        await using var dbContext = fixture.CreateDbContext();
        var service = CreateReportService(dbContext, member.Id, TimeProvider.System);

        var act = () => service.GetAsync();

        await act.Should().ThrowAsync<AdministratorRequiredException>();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [SqlServerFact]
    public async Task RetentionRunner_deletes_day_90_rows_and_keeps_day_89_rows()
    {
        var today = ProductActivityCohortRules.UtcToday(TimeProvider.System);
        var fixedNow = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var expiredOwner = await TestDataFactory.CreateOwnerAsync(fixture);
        var retainedOwner = await TestDataFactory.CreateOwnerAsync(fixture);
        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Set<ProductActivity>().AddRange(
                NewActivity(expiredOwner, today.AddDays(-90), returned: true),
                NewActivity(retainedOwner, today.AddDays(-89), returned: false));
            await seedContext.SaveChangesAsync();
        }

        await using var dbContext = fixture.CreateDbContext();
        var runner = new ProductMetricsRetentionRunner(
            dbContext,
            Options(enabled: true),
            new ProductMetricsCollectionGate(),
            new FixedTimeProvider(fixedNow));

        var removedCount = await runner.RunAsync();

        removedCount.Should().BeGreaterThanOrEqualTo(1);
        (await dbContext.Set<ProductActivity>().AsNoTracking()
            .AnyAsync(activity => activity.OwnerId == expiredOwner))
            .Should().BeFalse();
        var retained = await dbContext.Set<ProductActivity>().AsNoTracking()
            .Where(activity => activity.OwnerId == retainedOwner)
            .ToListAsync();
        retained.Should().ContainSingle()
            .Which.ReturnedInDays30To59.Should().BeFalse();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    private async Task<RelioUser> CreateUserAsync(bool administrator)
    {
        var id = TestDataFactory.NewOwnerId();
        var email = $"{id}@example.com";
        var user = new RelioUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
        };

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Users.Add(user);
        dbContext.RelationshipTypes.AddRange(RelationshipType.CreateDefaults(user.Id));
        await dbContext.SaveChangesAsync();

        if (administrator)
        {
            var role = await dbContext.Roles.AsNoTracking()
                .SingleAsync(candidate => candidate.Name == RelioRoles.Administrator);
            dbContext.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id });
            await dbContext.SaveChangesAsync();
        }

        return user;
    }

    private static ProductMetricsReportService CreateReportService(
        RelioDbContext dbContext,
        string? userId,
        TimeProvider timeProvider) =>
        new(dbContext, new FakeCurrentUser(userId), Options(enabled: true), timeProvider);

    private static IOptionsMonitor<ProductMetricsOptions> Options(bool enabled) =>
        new TestOptionsMonitor(new ProductMetricsOptions { Enabled = enabled });

    private static ProductActivity NewActivity(string ownerId, DateOnly startedOn, bool returned) => new()
    {
        OwnerId = ownerId,
        CohortStartedOnUtc = startedOn,
        LastActiveOnUtc = returned ? startedOn.AddDays(30) : startedOn,
        ReturnedInDays30To59 = returned,
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

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class InsertProductActivityOnFirstSaveInterceptor(
        SqlServerDatabaseFixture fixture,
        string ownerId,
        DateOnly today) : SaveChangesInterceptor
    {
        private int _hasInserted;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _hasInserted, 1) == 0)
            {
                await using var competitorContext = fixture.CreateDbContext();
                competitorContext.Set<ProductActivity>()
                    .Add(NewActivity(ownerId, today, returned: false));
                await competitorContext.SaveChangesAsync(cancellationToken);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class InsertProductActivityIdCollisionOnFirstSaveInterceptor(
        SqlServerDatabaseFixture fixture,
        string otherOwnerId,
        DateOnly today) : SaveChangesInterceptor
    {
        private int _hasInserted;

        public string OtherOwnerId { get; } = otherOwnerId;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _hasInserted, 1) == 0)
            {
                var pending = eventData.Context!.ChangeTracker.Entries<ProductActivity>()
                    .Single()
                    .Entity;
                var collision = NewActivity(OtherOwnerId, today, returned: false);
                collision.Id = pending.Id;

                await using var competitorContext = fixture.CreateDbContext();
                competitorContext.Set<ProductActivity>().Add(collision);
                await competitorContext.SaveChangesAsync(cancellationToken);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
