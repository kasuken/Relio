using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Relio.Application.Administration;
using Relio.Data.Identity;
using Relio.Data.Seeding;
using Relio.Domain;

namespace Relio.Data.Tests.Seeding;

/// <summary>
/// Covers issue #15's demo data seeder requirements: idempotency, refusing to run in Production,
/// and that seeded data is correctly owned by the demo user (see the "User-scoped data pattern"
/// section of AGENTS.md).
/// </summary>
public class DemoDataSeederTests
{
    [Fact]
    public async Task SeedAsync_does_nothing_when_disabled()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var seeder = CreateSeeder(dbContext, userManager, enabled: false, environmentName: Environments.Development);

        await seeder.SeedAsync();

        (await userManager.FindByEmailAsync(DemoDataSeeder.DemoEmail)).Should().BeNull();
        (await dbContext.People.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task SeedAsync_creates_the_demo_user_and_sample_data_outside_production(string environmentName)
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var seeder = CreateSeeder(dbContext, userManager, enabled: true, environmentName);

        await seeder.SeedAsync();

        var demoUser = await userManager.FindByEmailAsync(DemoDataSeeder.DemoEmail);
        demoUser.Should().NotBeNull();
        demoUser!.EmailConfirmed.Should().BeTrue("the demo account must be able to sign in regardless of Email:Provider");

        (await userManager.CheckPasswordAsync(demoUser, DemoDataSeeder.DemoPassword)).Should().BeTrue();
        (await userManager.IsInRoleAsync(demoUser, RelioRoles.Administrator)).Should().BeTrue(
            "the demo account is the instance's Administrator");

        var people = await dbContext.People.Where(p => p.OwnerId == demoUser.Id).ToListAsync();
        people.Should().HaveCountGreaterThanOrEqualTo(6).And.HaveCountLessThanOrEqualTo(8);
        people.Should().ContainSingle(p => p.IsArchived, "the seeded data includes one archived person");
        people.Should().Contain(
            p => p.BirthdayYear == 1992 && p.BirthdayMonth == 2 && p.BirthdayDay == 29,
            "the seeded data includes a Feb 29 birthday");
        people.Should().Contain(
            p => p.BirthdayYear == null && p.BirthdayMonth == 3 && p.BirthdayDay == 14,
            "the seeded data includes a birthday without a year");
        people.Should().OnlyContain(p => p.OwnerId == demoUser.Id);
        people.Should().Contain(p => p.RelationshipTypeId != null).And.Contain(p => p.RelationshipTypeId == null);
        people.Should().Contain(p => p.Nickname != null);
        people.Should().Contain(p => p.HowWeMet != null && p.Details != null && p.Details.Contains('\n'));

        var types = await dbContext.RelationshipTypes.Where(t => t.OwnerId == demoUser.Id).OrderBy(t => t.SortOrder).ToListAsync();
        types.Select(t => t.Name).Should().Equal("Family", "Partner", "Friend", "Colleague", "Acquaintance", "Other");
        people.Where(p => p.RelationshipTypeId != null)
            .Should().OnlyContain(p => types.Any(t => t.Id == p.RelationshipTypeId));

        var tags = await dbContext.Tags.Where(t => t.OwnerId == demoUser.Id).ToListAsync();
        tags.Should().NotBeEmpty();

        var profile = await dbContext.UserProfiles.SingleOrDefaultAsync(p => p.OwnerId == demoUser.Id);
        profile.Should().NotBeNull();
        profile!.TimeZoneId.Should().Be(DemoDataSeeder.DemoTimeZoneId);
    }

    [Fact]
    public async Task SeedAsync_gives_some_people_a_last_contacted_date_and_leaves_others_never_contacted()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var seeder = CreateSeeder(dbContext, userManager, enabled: true, environmentName: Environments.Development);

        await seeder.SeedAsync();

        var people = await dbContext.People.ToListAsync();
        people.Should().Contain(p => p.LastContactedOn != null).And.Contain(p => p.LastContactedOn == null);
        people.Where(p => p.LastContactedOn != null).Select(p => p.LastContactedOn!.Value)
            .Distinct().Should().HaveCountGreaterThan(2, "the demo exercises more than one way of reading the date");

        // Counted back from today in the demo user's own zone, so nothing is in the future there.
        var today = Relio.Application.Time.UserCalendar.Today(
            TimeProvider.System, Relio.Application.Time.TimeZoneIds.Parse(DemoDataSeeder.DemoTimeZoneId));
        people.Where(p => p.LastContactedOn != null).Should().OnlyContain(p => p.LastContactedOn <= today.AddDays(1));
    }

    [Fact]
    public async Task SeedAsync_gives_some_people_valid_contact_methods_owned_by_the_demo_user()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var seeder = CreateSeeder(dbContext, userManager, enabled: true, environmentName: Environments.Development);

        await seeder.SeedAsync();

        var demoUser = await userManager.FindByEmailAsync(DemoDataSeeder.DemoEmail);
        var contactMethods = await dbContext.ContactMethods.AsNoTracking().ToListAsync();
        var people = await dbContext.People.AsNoTracking().Where(p => p.OwnerId == demoUser!.Id).ToListAsync();

        contactMethods.Should().NotBeEmpty();
        contactMethods.Should().OnlyContain(c => c.OwnerId == demoUser!.Id);
        contactMethods.Should().OnlyContain(c => people.Any(p => p.Id == c.PersonId), "every contact method belongs to one of the demo person's people");
        contactMethods.Select(c => c.Kind).Distinct().Should().HaveCountGreaterThan(2, "the demo shows more than one kind");
        contactMethods.Where(c => c.Kind == ContactMethodKind.Email)
            .Should().OnlyContain(c => c.Value.EndsWith("@example.com") || c.Value.EndsWith("@example.org") || c.Value.EndsWith("@example.net"),
                "demo data only ever uses reserved example domains");

        // Each one passes the same rules a user's input does, and its key is the one the rules compute.
        foreach (var contactMethod in contactMethods)
        {
            var input = new Relio.Application.People.ContactMethodInput(null, contactMethod.Kind, contactMethod.Label, contactMethod.Value);
            Relio.Application.People.ContactMethodRules.Validate(input).Should().BeEmpty();
            contactMethod.NormalizedValue.Should().Be(
                Relio.Application.People.ContactMethodRules.ToNormalizedValue(contactMethod.Kind, contactMethod.Value));
        }

        // Per person, positions run 0, 1, 2, ... with no gaps or repeats.
        foreach (var group in contactMethods.GroupBy(c => c.PersonId))
        {
            group.Select(c => c.SortOrder).Order().Should().Equal(Enumerable.Range(0, group.Count()));
        }
    }

    [Fact]
    public async Task SeedAsync_refuses_and_logs_an_error_in_production()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var seeder = CreateSeeder(dbContext, userManager, enabled: true, environmentName: Environments.Production);

        await seeder.SeedAsync();

        (await userManager.FindByEmailAsync(DemoDataSeeder.DemoEmail)).Should().BeNull();
        (await dbContext.People.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_is_idempotent()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var seeder = CreateSeeder(dbContext, userManager, enabled: true, environmentName: Environments.Development);

        await seeder.SeedAsync();
        var peopleCountAfterFirstRun = await dbContext.People.CountAsync();
        var userCountAfterFirstRun = await dbContext.Users.CountAsync();
        var typeCountAfterFirstRun = await dbContext.RelationshipTypes.CountAsync();

        await seeder.SeedAsync();

        (await dbContext.People.CountAsync()).Should().Be(peopleCountAfterFirstRun);
        (await dbContext.RelationshipTypes.CountAsync()).Should().Be(typeCountAfterFirstRun).And.Be(6);
        (await dbContext.Users.CountAsync()).Should().Be(userCountAfterFirstRun);
        (await userManager.GetUsersInRoleAsync(RelioRoles.Administrator)).Should().ContainSingle(
            "the demo user is made an Administrator exactly once");
        (await dbContext.UserRoles.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SeedAsync_does_not_recreate_relationship_types_the_demo_user_removed()
    {
        // Issue #25: a default the user removed in settings must stay removed on the next start.
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var seeder = CreateSeeder(dbContext, userManager, enabled: true, environmentName: Environments.Development);
        await seeder.SeedAsync();
        var demoUser = (await userManager.FindByEmailAsync(DemoDataSeeder.DemoEmail))!;
        var service = new Relio.Data.People.RelationshipTypeService(
            dbContext, new Relio.Data.Tests.People.FakeCurrentUser(demoUser.Id));
        var other = (await service.ListAsync()).Single(t => t.Name == "Other");
        await service.DeleteAsync(other.Id, null);

        await seeder.SeedAsync();

        (await service.ListAsync()).Select(t => t.Name).Should().NotContain("Other").And.HaveCount(5);
    }

    [Fact]
    public async Task SeedAsync_reuses_relationship_types_that_already_exist_for_the_demo_user()
    {
        // A demo database upgraded by the AddPersonProfile migration: the types exist, the people do not yet.
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var demoUser = new RelioUser { UserName = DemoDataSeeder.DemoEmail, Email = DemoDataSeeder.DemoEmail };
        (await userManager.CreateAsync(demoUser, DemoDataSeeder.DemoPassword)).Succeeded.Should().BeTrue();
        dbContext.RelationshipTypes.AddRange(RelationshipType.CreateDefaults(demoUser.Id));
        await dbContext.SaveChangesAsync();
        var seeder = CreateSeeder(dbContext, userManager, enabled: true, environmentName: Environments.Development);

        await seeder.SeedAsync();

        (await dbContext.RelationshipTypes.Where(t => t.OwnerId == demoUser.Id).CountAsync()).Should().Be(6);
        (await dbContext.People.Where(p => p.OwnerId == demoUser.Id).CountAsync(p => p.RelationshipTypeId != null))
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SeedAsync_makes_an_existing_demo_user_without_the_role_an_Administrator()
    {
        await using var dbContext = CreateDbContext();
        var userManager = UserManagerTestFactory.Create(dbContext);
        var seeder = CreateSeeder(dbContext, userManager, enabled: true, environmentName: Environments.Development);
        await seeder.SeedAsync();
        var demoUser = (await userManager.FindByEmailAsync(DemoDataSeeder.DemoEmail))!;
        await userManager.RemoveFromRoleAsync(demoUser, RelioRoles.Administrator);

        // A demo database created before issue #19: the user and its people exist, the role does not.
        await seeder.SeedAsync();

        (await userManager.IsInRoleAsync(demoUser, RelioRoles.Administrator)).Should().BeTrue();
    }

    private static RelioDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dbContext = new RelioDbContext(options, TimeProvider.System, FieldProtector);

        // Applies the seeded Administrator role (HasData), which AddToRoleAsync needs - InMemory has no migrations.
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static DemoDataSeeder CreateSeeder(
        RelioDbContext dbContext,
        UserManager<RelioUser> userManager,
        bool enabled,
        string environmentName) =>
        new(
            dbContext,
            userManager,
            TimeProvider.System,
            Options.Create(new DemoDataOptions { Enabled = enabled }),
            new FakeHostEnvironment(environmentName),
            NullLogger<DemoDataSeeder>.Instance);
}
