using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Billing;
using Relio.Application.People;
using Relio.Application.People.Import;
using Relio.Data.Billing;
using Relio.Data.People;
using Relio.Data.Tests.People;
using Relio.Domain;
using static Relio.Data.Tests.Billing.BillingTestHarness;

namespace Relio.Data.Tests.Billing;

/// <summary>
/// The free plan's active-people limit, enforced inside the services that add or restore people.
/// Every test that hits the limit also proves nothing was saved.
/// </summary>
public sealed class PlanLimitsTests
{
    private const string Owner = "limit-owner";
    private const int Limit = PlanCatalog.FreeActivePeopleLimit;

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeBillingProvider _provider = new();

    [Fact]
    public async Task Creating_a_person_at_the_limit_is_refused_and_saves_nothing()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddPeopleAsync(dbContext, Owner, active: Limit);

        var act = () => CreatePeopleService(dbContext).CreateAsync(new CreatePersonRequest
        {
            FirstName = "One too many",
            NewTagNames = ["new tag"],
        });

        (await act.Should().ThrowAsync<PlanLimitReachedException>()).Which.Limit.Should().Be(Limit);
        (await CountPeopleAsync(dbContext)).Should().Be(Limit);
        (await dbContext.Tags.AnyAsync(tag => tag.OwnerId == Owner)).Should().BeFalse("an abandoned save leaves no stray tag");
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task Archived_people_do_not_count_towards_the_limit()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddPeopleAsync(dbContext, Owner, active: Limit - 1, archived: 10);

        await CreatePeopleService(dbContext).CreateAsync(new CreatePersonRequest { FirstName = "The last one" });

        (await CountPeopleAsync(dbContext)).Should().Be(Limit + 10);
    }

    [Fact]
    public async Task Another_users_people_do_not_count_towards_mine()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddPeopleAsync(dbContext, "someone-else", active: Limit + 5);

        await CreatePeopleService(dbContext).CreateAsync(new CreatePersonRequest { FirstName = "Mine" });

        (await dbContext.People.CountAsync(person => person.OwnerId == Owner)).Should().Be(1);
    }

    [Fact]
    public async Task Without_billing_there_is_no_limit()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddPeopleAsync(dbContext, Owner, active: Limit + 10);
        _provider.IsEnabled = false;

        await CreatePeopleService(dbContext).CreateAsync(new CreatePersonRequest { FirstName = "Still fine" });
        await new PeopleService(dbContext, new FakeCurrentUser(Owner), _clock)
            .CreateAsync(new CreatePersonRequest { FirstName = "No limits object at all" });

        (await CountPeopleAsync(dbContext)).Should().Be(Limit + 12);
    }

    [Fact]
    public async Task Relio_Pro_has_no_limit()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddPeopleAsync(dbContext, Owner, active: Limit);
        await AddSubscriptionAsync(dbContext, Pro(Owner));

        await CreatePeopleService(dbContext).CreateAsync(new CreatePersonRequest { FirstName = "Pro person" });

        (await CountPeopleAsync(dbContext)).Should().Be(Limit + 1);
    }

    [Fact]
    public async Task A_suspended_Pro_subscription_is_limited_like_the_free_plan()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddPeopleAsync(dbContext, Owner, active: Limit);
        await AddSubscriptionAsync(dbContext, Pro(Owner, graceEndsAtUtc: Now.UtcDateTime.AddMinutes(-1)));

        var act = () => CreatePeopleService(dbContext).CreateAsync(new CreatePersonRequest { FirstName = "Suspended" });

        await act.Should().ThrowAsync<PlanLimitReachedException>();
    }

    [Fact]
    public async Task Restoring_a_person_at_the_limit_is_refused_and_they_stay_archived()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddPeopleAsync(dbContext, Owner, active: Limit, archived: 1);
        var archivedId = await dbContext.People.Where(person => person.IsArchived).Select(person => person.Id).SingleAsync();

        var act = () => CreatePeopleService(dbContext).RestoreAsync(archivedId);

        await act.Should().ThrowAsync<PlanLimitReachedException>();
        (await dbContext.People.AsNoTracking().SingleAsync(person => person.Id == archivedId)).IsArchived.Should().BeTrue();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task Restoring_someone_already_active_is_not_counted_again()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddPeopleAsync(dbContext, Owner, active: Limit);
        var activeId = await dbContext.People.Select(person => person.Id).FirstAsync();

        (await CreatePeopleService(dbContext).RestoreAsync(activeId)).Should().BeTrue();
    }

    [Fact]
    public async Task An_import_that_does_not_fit_imports_nobody()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddPeopleAsync(dbContext, Owner, active: Limit - 2);
        var service = new PeopleImportService(dbContext, new FakeCurrentUser(Owner), _clock, new PlanLimits(_provider));
        ImportPersonRequest[] requests =
        [
            new() { FirstName = "Ada" },
            new() { FirstName = "Grace" },
            new() { FirstName = "Hedy" },
        ];

        var act = () => service.ImportAsync(requests);

        await act.Should().ThrowAsync<PlanLimitReachedException>();
        (await CountPeopleAsync(dbContext)).Should().Be(Limit - 2);
    }

    [Fact]
    public async Task An_import_that_fits_exactly_is_imported()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddPeopleAsync(dbContext, Owner, active: Limit - 2);
        var service = new PeopleImportService(dbContext, new FakeCurrentUser(Owner), _clock, new PlanLimits(_provider));

        var created = await service.ImportAsync([new() { FirstName = "Ada" }, new() { FirstName = "Grace" }]);

        created.Should().Be(2);
        (await CountPeopleAsync(dbContext)).Should().Be(Limit);
    }

    [Fact]
    public async Task Merging_two_active_profiles_at_the_limit_is_allowed_because_it_lowers_the_count()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddPeopleAsync(dbContext, Owner, active: Limit - 2);
        var primary = new Person { OwnerId = Owner, FirstName = "Ada" };
        var duplicate = new Person { OwnerId = Owner, FirstName = "Ada" };
        dbContext.People.AddRange(primary, duplicate);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var service = new PersonMergeService(dbContext, new FakeCurrentUser(Owner), _clock, new PlanLimits(_provider));

        var outcome = await service.MergeAsync(new MergePeopleRequest
        {
            PrimaryId = primary.Id,
            DuplicateId = duplicate.Id,
            FieldChoices = new Dictionary<MergeField, MergeFieldChoice>(),
        });

        outcome.Should().Be(MergeOutcome.Merged);
        (await CountPeopleAsync(dbContext)).Should().Be(Limit - 1);
    }

    private PeopleService CreatePeopleService(RelioDbContext dbContext) =>
        new(dbContext, new FakeCurrentUser(Owner), _clock, new PlanLimits(_provider));

    private static Task<int> CountPeopleAsync(RelioDbContext dbContext) =>
        dbContext.People.AsNoTracking().CountAsync(person => person.OwnerId == Owner);
}
