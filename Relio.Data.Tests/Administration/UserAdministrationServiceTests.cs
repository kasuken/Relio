using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Administration;
using Relio.Application.Security;
using Relio.Data.Administration;
using Relio.Data.Tests.Seeding;
using Relio.Domain;
using static Relio.Data.Tests.Administration.AdministrationTestHarness;

namespace Relio.Data.Tests.Administration;

/// <summary>
/// Proves issue #19's administration rules through <see cref="UserAdministrationService"/>: only an
/// active Administrator can use it (checked against the database, not claims), an Administrator can
/// not lock themselves out, invitations store only a hash, and - like the rest of Relio - it never
/// reaches anyone's owned data.
/// </summary>
public class UserAdministrationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Every_method_requires_an_Administrator()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        var regular = await CreateUserAsync(dbContext, "regular@example.com");
        var service = CreateAdministrationService(dbContext, regular.Id);

        var calls = new Dictionary<string, Func<Task>>
        {
            [nameof(IUserAdministrationService.ListAccountsAsync)] = () => service.ListAccountsAsync(),
            [nameof(IUserAdministrationService.DisableAccountAsync)] = () => service.DisableAccountAsync("anyone"),
            [nameof(IUserAdministrationService.EnableAccountAsync)] = () => service.EnableAccountAsync("anyone"),
            [nameof(IUserAdministrationService.ListPendingInvitationsAsync)] = () => service.ListPendingInvitationsAsync(),
            [nameof(IUserAdministrationService.CreateInvitationAsync)] = () => service.CreateInvitationAsync("x@example.com"),
            [nameof(IUserAdministrationService.RevokeInvitationAsync)] = () => service.RevokeInvitationAsync(Guid.NewGuid()),
        };

        // Guards against a new method being added to the interface without a check here.
        calls.Keys.Should().BeEquivalentTo(typeof(IUserAdministrationService).GetMethods().Select(m => m.Name));

        foreach (var (name, call) in calls)
        {
            await call.Should().ThrowAsync<AdministratorRequiredException>(because: name);
        }
    }

    [Fact]
    public async Task Unknown_caller_is_refused()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = CreateAdministrationService(dbContext, "no-such-user");

        await service.Invoking(s => s.ListAccountsAsync()).Should().ThrowAsync<AdministratorRequiredException>();
    }

    [Fact]
    public async Task Unauthenticated_caller_throws_UnauthenticatedUserException()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = CreateAdministrationService(dbContext, currentUserId: null);

        await service.Invoking(s => s.ListAccountsAsync()).Should().ThrowAsync<UnauthenticatedUserException>();
    }

    [Fact]
    public async Task Disabled_administrator_cannot_administer()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var admin = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        admin.IsDisabled = true;
        await dbContext.SaveChangesAsync();
        var service = CreateAdministrationService(dbContext, admin.Id);

        await service.Invoking(s => s.ListAccountsAsync()).Should().ThrowAsync<AdministratorRequiredException>();
    }

    [Fact]
    public async Task Administrator_check_reads_the_database_not_a_copy_this_context_already_tracks()
    {
        // An interactive circuit keeps one DbContext for its whole life. Disabling the
        // administrator from elsewhere must still stop them on their very next call.
        var database = NewDatabase();
        await using var circuitContext = CreateDbContext(database);
        var admin = await CreateUserAsync(circuitContext, "admin@example.com", administrator: true);
        var service = CreateAdministrationService(circuitContext, admin.Id);
        await service.ListAccountsAsync();
        (await UserManagerTestFactory.Create(circuitContext).FindByIdAsync(admin.Id)).Should().NotBeNull(); // tracked

        await using (var otherContext = CreateDbContext(database))
        {
            var sameAdmin = await otherContext.Users.SingleAsync(u => u.Id == admin.Id);
            sameAdmin.IsDisabled = true;
            await otherContext.SaveChangesAsync();
        }

        await service.Invoking(s => s.ListAccountsAsync()).Should().ThrowAsync<AdministratorRequiredException>();
    }

    [Fact]
    public async Task Removing_the_role_elsewhere_stops_the_administrator_on_the_next_call()
    {
        var database = NewDatabase();
        await using var circuitContext = CreateDbContext(database);
        var admin = await CreateUserAsync(circuitContext, "admin@example.com", administrator: true);
        var service = CreateAdministrationService(circuitContext, admin.Id);
        await service.ListAccountsAsync();

        await using (var otherContext = CreateDbContext(database))
        {
            var userManager = UserManagerTestFactory.Create(otherContext);
            var sameAdmin = (await userManager.FindByIdAsync(admin.Id))!;
            await userManager.RemoveFromRoleAsync(sameAdmin, RelioRoles.Administrator);
        }

        await service.Invoking(s => s.ListAccountsAsync()).Should().ThrowAsync<AdministratorRequiredException>();
    }

    [Fact]
    public async Task DisableAccountAsync_works_on_an_account_that_changed_elsewhere_since_it_was_loaded()
    {
        // A failed sign-in elsewhere bumps the account's concurrency stamp; a long-lived context
        // that tracked the account earlier must not fail to save because of it.
        var database = NewDatabase();
        await using var circuitContext = CreateDbContext(database);
        var admin = await CreateUserAsync(circuitContext, "admin@example.com", administrator: true);
        var target = await CreateUserAsync(circuitContext, "target@example.com");
        var service = CreateAdministrationService(circuitContext, admin.Id);
        (await UserManagerTestFactory.Create(circuitContext).FindByIdAsync(target.Id)).Should().NotBeNull(); // tracked

        await using (var otherContext = CreateDbContext(database))
        {
            var userManager = UserManagerTestFactory.Create(otherContext);
            await userManager.AccessFailedAsync((await userManager.FindByIdAsync(target.Id))!);
        }

        (await service.DisableAccountAsync(target.Id)).Should().Be(AccountChangeResult.Succeeded);
        await using var verify = CreateDbContext(database);
        (await verify.Users.AsNoTracking().SingleAsync(u => u.Id == target.Id)).IsDisabled.Should().BeTrue();
    }

    [Fact]
    public async Task ListAccountsAsync_returns_role_and_status_for_every_account()
    {
        var time = new FakeTimeProvider(Now);
        await using var dbContext = CreateDbContext(NewDatabase(), time);
        var admin = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        await CreateUserAsync(dbContext, "b-regular@example.com");
        var disabled = await CreateUserAsync(dbContext, "c-disabled@example.com");
        disabled.IsDisabled = true;
        var locked = await CreateUserAsync(dbContext, "d-locked@example.com");
        locked.LockoutEnd = Now.AddMinutes(10);
        var lockExpired = await CreateUserAsync(dbContext, "e-lock-over@example.com");
        lockExpired.LockoutEnd = Now.AddMinutes(-10);
        await dbContext.SaveChangesAsync();
        var service = CreateAdministrationService(dbContext, admin.Id, timeProvider: time);

        var accounts = await service.ListAccountsAsync();

        accounts.Select(a => a.Email).Should().Equal(
            "admin@example.com", "b-regular@example.com", "c-disabled@example.com", "d-locked@example.com", "e-lock-over@example.com");
        accounts.Single(a => a.Email == "admin@example.com").Should().Match<AccountSummary>(
            a => a.IsAdministrator && a.IsCurrentUser && !a.IsDisabled && !a.IsLockedOut);
        accounts.Single(a => a.Email == "b-regular@example.com").Should().Match<AccountSummary>(
            a => !a.IsAdministrator && !a.IsCurrentUser);
        accounts.Single(a => a.Email == "c-disabled@example.com").IsDisabled.Should().BeTrue();
        accounts.Single(a => a.Email == "d-locked@example.com").IsLockedOut.Should().BeTrue();
        accounts.Single(a => a.Email == "e-lock-over@example.com").IsLockedOut.Should().BeFalse();
    }

    [Fact]
    public async Task DisableAccountAsync_sets_the_flag_and_rotates_the_security_stamp()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var admin = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        var target = await CreateUserAsync(dbContext, "target@example.com");
        var stampBefore = target.SecurityStamp;
        var service = CreateAdministrationService(dbContext, admin.Id);

        var result = await service.DisableAccountAsync(target.Id);

        result.Should().Be(AccountChangeResult.Succeeded);
        var reloaded = await dbContext.Users.AsNoTracking().SingleAsync(u => u.Id == target.Id);
        reloaded.IsDisabled.Should().BeTrue();
        reloaded.SecurityStamp.Should().NotBe(stampBefore, "rotating the stamp is what ends open sessions");
    }

    [Fact]
    public async Task DisableAccountAsync_refuses_the_callers_own_account()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var admin = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        var service = CreateAdministrationService(dbContext, admin.Id);

        var result = await service.DisableAccountAsync(admin.Id);

        result.Should().Be(AccountChangeResult.CannotChangeOwnAccount);
        (await dbContext.Users.AsNoTracking().SingleAsync(u => u.Id == admin.Id)).IsDisabled.Should().BeFalse();
    }

    [Fact]
    public async Task DisableAccountAsync_unknown_user_returns_NotFound()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var admin = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        var service = CreateAdministrationService(dbContext, admin.Id);

        (await service.DisableAccountAsync("missing")).Should().Be(AccountChangeResult.NotFound);
        (await service.EnableAccountAsync("missing")).Should().Be(AccountChangeResult.NotFound);
    }

    [Fact]
    public async Task DisableAccountAsync_twice_is_a_no_op_that_keeps_the_stamp()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var admin = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        var target = await CreateUserAsync(dbContext, "target@example.com");
        var service = CreateAdministrationService(dbContext, admin.Id);
        await service.DisableAccountAsync(target.Id);
        var stamp = (await dbContext.Users.AsNoTracking().SingleAsync(u => u.Id == target.Id)).SecurityStamp;

        (await service.DisableAccountAsync(target.Id)).Should().Be(AccountChangeResult.Succeeded);

        (await dbContext.Users.AsNoTracking().SingleAsync(u => u.Id == target.Id)).SecurityStamp.Should().Be(stamp);
    }

    [Fact]
    public async Task EnableAccountAsync_clears_the_flag()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var admin = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        var target = await CreateUserAsync(dbContext, "target@example.com");
        var service = CreateAdministrationService(dbContext, admin.Id);
        await service.DisableAccountAsync(target.Id);

        var result = await service.EnableAccountAsync(target.Id);

        result.Should().Be(AccountChangeResult.Succeeded);
        (await dbContext.Users.AsNoTracking().SingleAsync(u => u.Id == target.Id)).IsDisabled.Should().BeFalse();
    }

    [Fact]
    public async Task CreateInvitationAsync_stores_only_the_token_hash()
    {
        var time = new FakeTimeProvider(Now);
        await using var dbContext = CreateDbContext(NewDatabase(), time);
        var admin = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        var service = CreateAdministrationService(dbContext, admin.Id, timeProvider: time, invitationLifetime: TimeSpan.FromDays(3));

        var result = await service.CreateInvitationAsync("  Friend@Example.com ");

        result.Status.Should().Be(CreateInvitationStatus.Created);
        var created = result.Invitation!;
        created.Email.Should().Be("Friend@Example.com");
        created.Token.Should().NotBeNullOrWhiteSpace().And.MatchRegex("^[A-Za-z0-9_-]{43}$", "32 random bytes, base64url");
        created.ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddDays(3));

        var stored = await dbContext.RegistrationInvitations.AsNoTracking().SingleAsync();
        stored.TokenHash.Should().Be(InvitationTokens.Hash(created.Token)).And.NotBe(created.Token);
        stored.TokenHash.Should().HaveLength(64);
        new[] { stored.Email, stored.NormalizedEmail, stored.TokenHash, stored.CreatedByUserId }
            .Should().NotContain(value => value.Contains(created.Token));
        stored.CreatedByUserId.Should().Be(admin.Id);
    }

    [Fact]
    public async Task Each_invitation_gets_a_different_token()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var admin = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        var service = CreateAdministrationService(dbContext, admin.Id);

        var first = await service.CreateInvitationAsync("one@example.com");
        var second = await service.CreateInvitationAsync("two@example.com");

        first.Invitation!.Token.Should().NotBe(second.Invitation!.Token);
    }

    [Theory]
    [InlineData(RegistrationMode.Open)]
    [InlineData(RegistrationMode.Closed)]
    public async Task CreateInvitationAsync_refuses_outside_InviteOnly(RegistrationMode mode)
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var admin = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        var service = CreateAdministrationService(dbContext, admin.Id, mode);

        var result = await service.CreateInvitationAsync("friend@example.com");

        result.Status.Should().Be(CreateInvitationStatus.InvitationsNotInUse);
        result.Invitation.Should().BeNull();
        (await dbContext.RegistrationInvitations.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public async Task CreateInvitationAsync_rejects_an_invalid_email(string email)
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var admin = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        var service = CreateAdministrationService(dbContext, admin.Id);

        (await service.CreateInvitationAsync(email)).Status.Should().Be(CreateInvitationStatus.InvalidEmail);
    }

    [Fact]
    public async Task CreateInvitationAsync_refuses_an_existing_account_email()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var admin = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        await CreateUserAsync(dbContext, "member@example.com");
        var service = CreateAdministrationService(dbContext, admin.Id);

        var result = await service.CreateInvitationAsync("MEMBER@example.com");

        result.Status.Should().Be(CreateInvitationStatus.AlreadyHasAccount);
        (await dbContext.RegistrationInvitations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateInvitationAsync_replaces_a_pending_invitation_for_the_same_email()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var admin = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        var service = CreateAdministrationService(dbContext, admin.Id);
        var first = (await service.CreateInvitationAsync("friend@example.com")).Invitation!;

        var second = (await service.CreateInvitationAsync("FRIEND@example.com")).Invitation!;

        var pending = await service.ListPendingInvitationsAsync();
        pending.Should().ContainSingle().Which.Id.Should().Be(second.Id);
        (await dbContext.RegistrationInvitations.AnyAsync(i => i.Id == first.Id)).Should().BeFalse();

        // The first link no longer works.
        var registration = CreateRegistrationService(dbContext, RegistrationMode.InviteOnly);
        (await registration.GetEligibilityAsync(first.Token)).Access.Should().Be(RegistrationAccess.InvitationInvalid);
        (await registration.GetEligibilityAsync(second.Token)).Access.Should().Be(RegistrationAccess.Allowed);
    }

    [Fact]
    public async Task Expired_invitations_are_purged()
    {
        var time = new FakeTimeProvider(Now);
        await using var dbContext = CreateDbContext(NewDatabase(), time);
        var admin = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        await AddInvitationAsync(dbContext, "old@example.com", Now.UtcDateTime.AddDays(1));
        await AddInvitationAsync(dbContext, "fresh@example.com", Now.UtcDateTime.AddDays(10));
        var service = CreateAdministrationService(dbContext, admin.Id, timeProvider: time);
        time.Advance(TimeSpan.FromDays(2));

        var pending = await service.ListPendingInvitationsAsync();

        pending.Should().ContainSingle().Which.Email.Should().Be("fresh@example.com");
        (await dbContext.RegistrationInvitations.CountAsync()).Should().Be(1, "the expired row is deleted, not just hidden");
    }

    [Fact]
    public async Task RevokeInvitationAsync_deletes_the_invitation()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var admin = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        var service = CreateAdministrationService(dbContext, admin.Id);
        var invitation = (await service.CreateInvitationAsync("friend@example.com")).Invitation!;

        (await service.RevokeInvitationAsync(invitation.Id)).Should().BeTrue();
        (await service.RevokeInvitationAsync(invitation.Id)).Should().BeFalse("it is already gone");

        (await service.ListPendingInvitationsAsync()).Should().BeEmpty();
        var registration = CreateRegistrationService(dbContext, RegistrationMode.InviteOnly);
        (await registration.GetEligibilityAsync(invitation.Token)).Access.Should().Be(RegistrationAccess.InvitationInvalid);
    }

    [Fact]
    public async Task Administration_never_changes_owned_data()
    {
        var time = new FakeTimeProvider(Now);
        await using var dbContext = CreateDbContext(NewDatabase(), time);
        var admin = await CreateUserAsync(dbContext, "admin@example.com", administrator: true);
        var member = await CreateUserAsync(dbContext, "member@example.com");

        var tag = new Tag { OwnerId = member.Id, Name = "Family" };
        dbContext.Tags.Add(tag);
        dbContext.People.Add(new Person { OwnerId = member.Id, FirstName = "Marta", LastName = "Rossi", Tags = { tag } });
        dbContext.UserProfiles.Add(new UserProfile { OwnerId = member.Id, TimeZoneId = "Europe/Rome", DisplayName = "Marta" });
        await dbContext.SaveChangesAsync();

        async Task<object> SnapshotAsync() => new
        {
            People = await dbContext.People.AsNoTracking().OrderBy(p => p.Id)
                .Select(p => new { p.Id, p.OwnerId, p.FirstName, p.LastName, p.IsArchived, p.UpdatedAtUtc }).ToListAsync(),
            Tags = await dbContext.Tags.AsNoTracking().OrderBy(t => t.Id)
                .Select(t => new { t.Id, t.OwnerId, t.Name, t.UpdatedAtUtc }).ToListAsync(),
            Profiles = await dbContext.UserProfiles.AsNoTracking().OrderBy(p => p.Id)
                .Select(p => new { p.Id, p.OwnerId, p.TimeZoneId, p.DisplayName, p.UpdatedAtUtc }).ToListAsync(),
        };

        var before = await SnapshotAsync();
        time.Advance(TimeSpan.FromHours(5)); // any write through this context would stamp a new UpdatedAtUtc

        var service = CreateAdministrationService(dbContext, admin.Id, timeProvider: time);
        await service.ListAccountsAsync();
        await service.DisableAccountAsync(member.Id);
        await service.EnableAccountAsync(member.Id);
        var invitation = (await service.CreateInvitationAsync("friend@example.com")).Invitation!;
        await service.ListPendingInvitationsAsync();
        await service.RevokeInvitationAsync(invitation.Id);

        (await SnapshotAsync()).Should().BeEquivalentTo(before);
    }
}
