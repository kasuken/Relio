using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Administration;
using Relio.Data.Tests.Seeding;
using static Relio.Data.Tests.Administration.AdministrationTestHarness;

namespace Relio.Data.Tests.Administration;

/// <summary>
/// Covers <c>Administration:AdministratorEmail</c> (issue #19): how an instance that predates the
/// first-account rule, or one with no Administrator left, gets one.
/// </summary>
public class AdministratorBootstrapperTests
{
    [Fact]
    public async Task Promotes_the_configured_account()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var user = await CreateUserAsync(dbContext, "owner@example.com");
        await CreateUserAsync(dbContext, "other@example.com");

        await CreateBootstrapper(dbContext, "OWNER@example.com").RunAsync();

        var userManager = UserManagerTestFactory.Create(dbContext);
        (await userManager.IsInRoleAsync(user, RelioRoles.Administrator)).Should().BeTrue();
        (await userManager.GetUsersInRoleAsync(RelioRoles.Administrator)).Should().ContainSingle();
    }

    [Fact]
    public async Task Is_idempotent()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        await CreateUserAsync(dbContext, "owner@example.com");
        var bootstrapper = CreateBootstrapper(dbContext, "owner@example.com");

        await bootstrapper.RunAsync();
        await bootstrapper.RunAsync();

        (await dbContext.UserRoles.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Does_nothing_when_unset(string? configured)
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        await CreateUserAsync(dbContext, "owner@example.com");

        await CreateBootstrapper(dbContext, configured).RunAsync();

        (await dbContext.UserRoles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Configured_email_without_an_account_promotes_nobody()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        await CreateUserAsync(dbContext, "owner@example.com");

        await CreateBootstrapper(dbContext, "nobody@example.com").RunAsync();

        (await dbContext.UserRoles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Purges_expired_invitations_and_keeps_pending_ones()
    {
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(now);
        await using var dbContext = CreateDbContext(NewDatabase(), time);
        await AddInvitationAsync(dbContext, "old@example.com", now.UtcDateTime.AddDays(-1));
        await AddInvitationAsync(dbContext, "fresh@example.com", now.UtcDateTime.AddDays(1));

        await CreateBootstrapper(dbContext, null, time).RunAsync();

        (await dbContext.RegistrationInvitations.Select(i => i.Email).ToListAsync()).Should().Equal("fresh@example.com");
    }

    [Fact]
    public async Task Runs_on_an_empty_instance_without_error()
    {
        await using var dbContext = CreateDbContext(NewDatabase());

        await CreateBootstrapper(dbContext, "owner@example.com").Invoking(b => b.RunAsync()).Should().NotThrowAsync();
    }
}
