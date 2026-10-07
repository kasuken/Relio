using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Administration;
using Relio.Data.Administration;
using Relio.Data.Tests.Seeding;
using static Relio.Data.Tests.Administration.AdministrationTestHarness;

namespace Relio.Data.Tests.Administration;

/// <summary>
/// Proves issue #19's registration rules through <see cref="AccountRegistrationService"/>: the
/// first account becomes the Administrator (and only it, even under concurrency), and
/// <c>Registration:Mode</c> is enforced by the service - not just by the page - so a crafted post
/// can never create an account the instance's rules refuse.
/// </summary>
public class AccountRegistrationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task First_account_on_an_empty_instance_becomes_Administrator()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = CreateRegistrationService(dbContext, RegistrationMode.Open);

        var result = await service.RegisterAsync(new("first@example.com", StrongPassword, null, null));

        result.Succeeded.Should().BeTrue();
        result.IsAdministrator.Should().BeTrue();
        var user = await UserManagerTestFactory.Create(dbContext).FindByIdAsync(result.UserId!);
        (await UserManagerTestFactory.Create(dbContext).IsInRoleAsync(user!, RelioRoles.Administrator)).Should().BeTrue();
        (await dbContext.UserProfiles.AsNoTracking().SingleAsync(profile => profile.OwnerId == result.UserId))
            .OnboardingDismissed.Should().BeFalse();
    }

    [Fact]
    public async Task Second_account_is_not_an_Administrator()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = CreateRegistrationService(dbContext, RegistrationMode.Open);
        await service.RegisterAsync(new("first@example.com", StrongPassword, null, null));

        var second = await service.RegisterAsync(new("second@example.com", StrongPassword, null, null));

        second.Succeeded.Should().BeTrue();
        second.IsAdministrator.Should().BeFalse();
        (await UserManagerTestFactory.Create(dbContext).GetUsersInRoleAsync(RelioRoles.Administrator))
            .Should().ContainSingle();
        (await dbContext.UserProfiles.AsNoTracking().SingleAsync(profile => profile.OwnerId == second.UserId))
            .OnboardingDismissed.Should().BeFalse();
    }

    [Fact]
    public async Task Existing_accounts_without_an_Administrator_do_not_make_the_next_registration_one()
    {
        // An instance that predates issue #19: accounts exist, nobody is an Administrator. Whoever
        // registers next must not be able to claim the instance (that is what
        // Administration:AdministratorEmail is for).
        await using var dbContext = CreateDbContext(NewDatabase());
        await CreateUserAsync(dbContext, "old-timer@example.com");
        var service = CreateRegistrationService(dbContext, RegistrationMode.Open);

        var result = await service.RegisterAsync(new("stranger@example.com", StrongPassword, null, null));

        result.Succeeded.Should().BeTrue();
        result.IsAdministrator.Should().BeFalse();
        (await UserManagerTestFactory.Create(dbContext).GetUsersInRoleAsync(RelioRoles.Administrator)).Should().BeEmpty();
    }

    [Fact]
    public async Task Concurrent_first_registrations_produce_exactly_one_Administrator()
    {
        var database = NewDatabase();
        var sharedLock = new RegistrationLock();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async i =>
        {
            // Each caller gets its own context and UserManager, like separate requests do.
            await using var dbContext = CreateDbContext(database);
            var service = CreateRegistrationService(dbContext, RegistrationMode.Open, registrationLock: sharedLock);
            return await service.RegisterAsync(new($"racer{i}@example.com", StrongPassword, null, null));
        }));

        results.Should().OnlyContain(r => r.Succeeded);
        results.Count(r => r.IsAdministrator).Should().Be(1);

        await using var verify = CreateDbContext(database);
        (await UserManagerTestFactory.Create(verify).GetUsersInRoleAsync(RelioRoles.Administrator)).Should().ContainSingle();
        (await verify.Users.CountAsync()).Should().Be(5);
    }

    [Fact]
    public async Task Closed_mode_refuses_registration_once_an_account_exists()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        await CreateUserAsync(dbContext, "owner@example.com", administrator: true);
        var service = CreateRegistrationService(dbContext, RegistrationMode.Closed);

        var result = await service.RegisterAsync(new("late@example.com", StrongPassword, null, null));

        result.Status.Should().Be(RegisterAccountStatus.Closed);
        result.UserId.Should().BeNull();
        (await dbContext.Users.CountAsync()).Should().Be(1);
        (await dbContext.UserProfiles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Closed_mode_still_allows_the_first_account()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = CreateRegistrationService(dbContext, RegistrationMode.Closed);

        var result = await service.RegisterAsync(new("first@example.com", StrongPassword, null, null));

        result.Succeeded.Should().BeTrue();
        result.IsAdministrator.Should().BeTrue();
    }

    [Fact]
    public async Task InviteOnly_still_allows_the_first_account_without_an_invitation()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = CreateRegistrationService(dbContext, RegistrationMode.InviteOnly);

        var result = await service.RegisterAsync(new("first@example.com", StrongPassword, null, null));

        result.Succeeded.Should().BeTrue();
        result.IsAdministrator.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InviteOnly_without_a_token_returns_InvitationRequired(string? token)
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        await CreateUserAsync(dbContext, "owner@example.com", administrator: true);
        var service = CreateRegistrationService(dbContext, RegistrationMode.InviteOnly);

        var result = await service.RegisterAsync(new("late@example.com", StrongPassword, null, token));

        result.Status.Should().Be(RegisterAccountStatus.InvitationRequired);
        (await dbContext.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task InviteOnly_with_an_unknown_token_returns_InvitationInvalid()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        await CreateUserAsync(dbContext, "owner@example.com", administrator: true);
        var service = CreateRegistrationService(dbContext, RegistrationMode.InviteOnly);

        var result = await service.RegisterAsync(new("late@example.com", StrongPassword, null, "not-a-real-token"));

        result.Status.Should().Be(RegisterAccountStatus.InvitationInvalid);
        (await dbContext.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task InviteOnly_with_an_expired_invitation_returns_InvitationInvalid()
    {
        var time = new FakeTimeProvider(Now);
        await using var dbContext = CreateDbContext(NewDatabase(), time);
        await CreateUserAsync(dbContext, "owner@example.com", administrator: true);
        var token = await AddInvitationAsync(dbContext, "late@example.com", Now.UtcDateTime.AddDays(1));
        var service = CreateRegistrationService(dbContext, RegistrationMode.InviteOnly, time);
        time.Advance(TimeSpan.FromDays(2));

        var result = await service.RegisterAsync(new("late@example.com", StrongPassword, null, token));

        result.Status.Should().Be(RegisterAccountStatus.InvitationInvalid);
        (await dbContext.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task InviteOnly_with_a_different_email_returns_InvitationEmailMismatch_and_keeps_the_invitation()
    {
        var time = new FakeTimeProvider(Now);
        await using var dbContext = CreateDbContext(NewDatabase(), time);
        await CreateUserAsync(dbContext, "owner@example.com", administrator: true);
        var token = await AddInvitationAsync(dbContext, "invited@example.com", Now.UtcDateTime.AddDays(7));
        var service = CreateRegistrationService(dbContext, RegistrationMode.InviteOnly, time);

        var mismatch = await service.RegisterAsync(new("someone-else@example.com", StrongPassword, null, token));

        mismatch.Status.Should().Be(RegisterAccountStatus.InvitationEmailMismatch);
        (await dbContext.Users.CountAsync()).Should().Be(1);
        (await dbContext.RegistrationInvitations.CountAsync()).Should().Be(1);

        // The invitation is still good for the address it was made for.
        var correct = await service.RegisterAsync(new("invited@example.com", StrongPassword, null, token));
        correct.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task InviteOnly_email_match_is_case_insensitive()
    {
        var time = new FakeTimeProvider(Now);
        await using var dbContext = CreateDbContext(NewDatabase(), time);
        await CreateUserAsync(dbContext, "owner@example.com", administrator: true);
        var token = await AddInvitationAsync(dbContext, "Invited@Example.com", Now.UtcDateTime.AddDays(7));
        var service = CreateRegistrationService(dbContext, RegistrationMode.InviteOnly, time);

        var result = await service.RegisterAsync(new("invited@EXAMPLE.com", StrongPassword, null, token));

        result.Succeeded.Should().BeTrue();
        result.IsAdministrator.Should().BeFalse();
    }

    [Fact]
    public async Task InviteOnly_valid_invitation_registers_and_deletes_the_invitation()
    {
        var time = new FakeTimeProvider(Now);
        await using var dbContext = CreateDbContext(NewDatabase(), time);
        await CreateUserAsync(dbContext, "owner@example.com", administrator: true);
        var token = await AddInvitationAsync(dbContext, "invited@example.com", Now.UtcDateTime.AddDays(7));
        var service = CreateRegistrationService(dbContext, RegistrationMode.InviteOnly, time);

        var result = await service.RegisterAsync(new("invited@example.com", StrongPassword, null, token));

        result.Succeeded.Should().BeTrue();
        (await dbContext.RegistrationInvitations.CountAsync()).Should().Be(0, "an invitation works once");

        var reuse = await service.RegisterAsync(new("invited@example.com", StrongPassword, null, token));
        reuse.Status.Should().Be(RegisterAccountStatus.InvitationInvalid);
    }

    [Fact]
    public async Task Open_mode_ignores_an_invitation_token()
    {
        var time = new FakeTimeProvider(Now);
        await using var dbContext = CreateDbContext(NewDatabase(), time);
        await CreateUserAsync(dbContext, "owner@example.com", administrator: true);
        var service = CreateRegistrationService(dbContext, RegistrationMode.Open, time);

        var result = await service.RegisterAsync(new("late@example.com", StrongPassword, null, "garbage"));

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Weak_password_creates_nothing_and_does_not_consume_the_invitation()
    {
        var time = new FakeTimeProvider(Now);
        await using var dbContext = CreateDbContext(NewDatabase(), time);
        await CreateUserAsync(dbContext, "owner@example.com", administrator: true);
        var token = await AddInvitationAsync(dbContext, "invited@example.com", Now.UtcDateTime.AddDays(7));
        var service = CreateRegistrationService(dbContext, RegistrationMode.InviteOnly, time);

        var result = await service.RegisterAsync(new("invited@example.com", "short", null, token));

        result.Status.Should().Be(RegisterAccountStatus.IdentityErrors);
        result.Errors.Should().NotBeEmpty();
        (await dbContext.Users.CountAsync()).Should().Be(1);
        (await dbContext.UserProfiles.CountAsync()).Should().Be(0);
        (await dbContext.RegistrationInvitations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_email_is_reported_as_an_Identity_error()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        await CreateUserAsync(dbContext, "owner@example.com", administrator: true);
        var service = CreateRegistrationService(dbContext, RegistrationMode.Open);

        var result = await service.RegisterAsync(new("owner@example.com", StrongPassword, null, null));

        result.Status.Should().Be(RegisterAccountStatus.IdentityErrors);
        result.Errors.Should().Contain(e => e.Code == "DuplicateUserName" || e.Code == "DuplicateEmail");
    }

    [Theory]
    [InlineData("Pacific/Kiritimati", "Pacific/Kiritimati")]
    [InlineData("Not/AZone", "UTC")]
    [InlineData(null, "UTC")]
    public async Task Registration_creates_the_profile_with_the_submitted_time_zone_or_UTC(
        string? submitted, string expected)
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = CreateRegistrationService(dbContext, RegistrationMode.Open);

        var result = await service.RegisterAsync(new("first@example.com", StrongPassword, submitted, null));

        var profile = await dbContext.UserProfiles.SingleAsync(p => p.OwnerId == result.UserId);
        profile.TimeZoneId.Should().Be(expected);
    }

    [Fact]
    public async Task GetEligibilityAsync_on_an_empty_instance_is_FirstAccount_in_every_mode()
    {
        foreach (var mode in Enum.GetValues<RegistrationMode>())
        {
            await using var dbContext = CreateDbContext(NewDatabase());
            var eligibility = await CreateRegistrationService(dbContext, mode).GetEligibilityAsync(null);

            eligibility.Access.Should().Be(RegistrationAccess.FirstAccount, $"mode {mode}");
            eligibility.CanRegister.Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(RegistrationMode.Open, RegistrationAccess.Allowed)]
    [InlineData(RegistrationMode.InviteOnly, RegistrationAccess.RequiresInvitation)]
    [InlineData(RegistrationMode.Closed, RegistrationAccess.Closed)]
    public async Task GetEligibilityAsync_reports_each_mode_once_an_account_exists(
        RegistrationMode mode, RegistrationAccess expected)
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        await CreateUserAsync(dbContext, "owner@example.com", administrator: true);

        var eligibility = await CreateRegistrationService(dbContext, mode).GetEligibilityAsync(null);

        eligibility.Access.Should().Be(expected);
        eligibility.CanRegister.Should().Be(expected == RegistrationAccess.Allowed);
    }

    [Fact]
    public async Task GetEligibilityAsync_with_a_valid_invitation_returns_the_invited_email()
    {
        var time = new FakeTimeProvider(Now);
        await using var dbContext = CreateDbContext(NewDatabase(), time);
        await CreateUserAsync(dbContext, "owner@example.com", administrator: true);
        var token = await AddInvitationAsync(dbContext, "invited@example.com", Now.UtcDateTime.AddDays(7));
        var service = CreateRegistrationService(dbContext, RegistrationMode.InviteOnly, time);

        var valid = await service.GetEligibilityAsync(token);
        var invalid = await service.GetEligibilityAsync("bogus");

        valid.Access.Should().Be(RegistrationAccess.Allowed);
        valid.InvitedEmail.Should().Be("invited@example.com");
        invalid.Access.Should().Be(RegistrationAccess.InvitationInvalid);
        invalid.InvitedEmail.Should().BeNull();
    }

    [Fact]
    public async Task Registering_seeds_the_default_relationship_types_for_the_new_account()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = CreateRegistrationService(dbContext, RegistrationMode.Open);
        await service.RegisterAsync(new("first@example.com", StrongPassword, null, null));

        var second = await service.RegisterAsync(new("second@example.com", StrongPassword, null, null));

        // Every account gets its own six - the first one and every later one - owned by that account.
        var types = await dbContext.RelationshipTypes.AsNoTracking()
            .Where(t => t.OwnerId == second.UserId)
            .OrderBy(t => t.SortOrder)
            .ToListAsync();
        types.Select(t => t.Name).Should().Equal("Family", "Partner", "Friend", "Colleague", "Acquaintance", "Other");
        (await dbContext.RelationshipTypes.CountAsync()).Should().Be(12);
    }

    [Fact]
    public async Task A_refused_registration_seeds_no_relationship_types()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        await CreateUserAsync(dbContext, "owner@example.com", administrator: true);
        var service = CreateRegistrationService(dbContext, RegistrationMode.Closed);

        await service.RegisterAsync(new("late@example.com", StrongPassword, null, null));
        await CreateRegistrationService(dbContext, RegistrationMode.Open).RegisterAsync(new("weak@example.com", "short", null, null));

        (await dbContext.RelationshipTypes.CountAsync()).Should().Be(0);
    }
}
