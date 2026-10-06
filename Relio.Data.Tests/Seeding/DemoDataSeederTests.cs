using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Relio.Application.Administration;
using Relio.Data.Identity;
using Relio.Data.Seeding;

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
        people.Should().Contain(p => p.Birthday == new DateOnly(1992, 2, 29), "the seeded data includes a Feb 29 birthday");
        people.Should().OnlyContain(p => p.OwnerId == demoUser.Id);

        var tags = await dbContext.Tags.Where(t => t.OwnerId == demoUser.Id).ToListAsync();
        tags.Should().NotBeEmpty();

        var profile = await dbContext.UserProfiles.SingleOrDefaultAsync(p => p.OwnerId == demoUser.Id);
        profile.Should().NotBeNull();
        profile!.TimeZoneId.Should().Be(DemoDataSeeder.DemoTimeZoneId);
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

        await seeder.SeedAsync();

        (await dbContext.People.CountAsync()).Should().Be(peopleCountAfterFirstRun);
        (await dbContext.Users.CountAsync()).Should().Be(userCountAfterFirstRun);
        (await userManager.GetUsersInRoleAsync(RelioRoles.Administrator)).Should().ContainSingle(
            "the demo user is made an Administrator exactly once");
        (await dbContext.UserRoles.CountAsync()).Should().Be(1);
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

        var dbContext = new RelioDbContext(options, TimeProvider.System);

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
