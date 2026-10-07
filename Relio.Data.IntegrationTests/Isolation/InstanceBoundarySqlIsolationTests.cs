using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Relio.Application.Administration;
using Relio.Application.Ownership;
using Relio.Application.Security;
using Relio.Data.Administration;
using Relio.Data.Identity;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Isolation;

[Collection(SqlServerCollection.Name)]
public sealed class InstanceBoundarySqlIsolationTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Administration_requires_an_active_administrator_and_never_changes_owned_memory()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture, makeOwnerAAdministrator: true);
        Guid personB;

        await using (var setup = fixture.CreateDbContext())
        {
            personB = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerB.Id, "Private B profile");
        }

        await using var ownerAScope = harness.AsOwnerA();
        using var identityA = ownerAScope.CreateIdentityServices();
        var managerA = identityA.GetRequiredService<UserManager<RelioUser>>();
        var administratorA = CreateAdministrationService(ownerAScope, managerA, RegistrationMode.InviteOnly);
        var summaries = await administratorA.ListAccountsAsync();
        summaries.Should().Contain(account => account.UserId == harness.OwnerA.Id
            && account.IsAdministrator
            && account.IsCurrentUser
            && !account.IsDisabled);
        summaries.Should().Contain(account => account.UserId == harness.OwnerB.Id
            && !account.IsAdministrator
            && !account.IsCurrentUser);

        (await administratorA.DisableAccountAsync(harness.OwnerA.Id))
            .Should().Be(AccountChangeResult.CannotChangeOwnAccount);
        (await administratorA.EnableAccountAsync(harness.OwnerA.Id))
            .Should().Be(AccountChangeResult.CannotChangeOwnAccount);
        (await administratorA.DisableAccountAsync("synthetic-missing-account"))
            .Should().Be(AccountChangeResult.NotFound);
        (await administratorA.EnableAccountAsync("synthetic-missing-account"))
            .Should().Be(AccountChangeResult.NotFound);

        await using (var nonAdministratorScope = harness.AsOwnerB())
        {
            using var identityB = nonAdministratorScope.CreateIdentityServices();
            var nonAdministrator = CreateAdministrationService(
                nonAdministratorScope,
                identityB.GetRequiredService<UserManager<RelioUser>>(),
                RegistrationMode.InviteOnly);

            await ThrowsAdministratorRequiredAsync(() => nonAdministrator.ListAccountsAsync());
            await ThrowsAdministratorRequiredAsync(
                () => nonAdministrator.DisableAccountAsync(harness.OwnerA.Id));
            await ThrowsAdministratorRequiredAsync(
                () => nonAdministrator.EnableAccountAsync(harness.OwnerA.Id));
            await ThrowsAdministratorRequiredAsync(
                () => nonAdministrator.ListPendingInvitationsAsync());
            await ThrowsAdministratorRequiredAsync(
                () => nonAdministrator.CreateInvitationAsync("blocked@example.com"));
            await ThrowsAdministratorRequiredAsync(
                () => nonAdministrator.RevokeInvitationAsync(Guid.NewGuid()));
        }

        var ownerB = await managerA.FindByIdAsync(harness.OwnerB.Id);
        ownerB.Should().NotBeNull();
        (await managerA.AddToRoleAsync(ownerB!, RelioRoles.Administrator)).Succeeded.Should().BeTrue();
        (await administratorA.DisableAccountAsync(harness.OwnerB.Id))
            .Should().Be(AccountChangeResult.Succeeded);
        var disabledSummary = (await administratorA.ListAccountsAsync())
            .Single(account => account.UserId == harness.OwnerB.Id);
        disabledSummary.IsAdministrator.Should().BeTrue();
        disabledSummary.IsDisabled.Should().BeTrue();

        await using (var disabledAdministratorScope = harness.AsOwnerB())
        {
            using var identityB = disabledAdministratorScope.CreateIdentityServices();
            var managerB = identityB.GetRequiredService<UserManager<RelioUser>>();
            var disabledAdministrator = CreateAdministrationService(
                disabledAdministratorScope,
                managerB,
                RegistrationMode.InviteOnly);

            await ThrowsAdministratorRequiredAsync(() => disabledAdministrator.ListAccountsAsync());
            await ThrowsAdministratorRequiredAsync(
                () => disabledAdministrator.DisableAccountAsync(harness.OwnerA.Id));
            await ThrowsAdministratorRequiredAsync(
                () => disabledAdministrator.EnableAccountAsync(harness.OwnerA.Id));
            await ThrowsAdministratorRequiredAsync(
                () => disabledAdministrator.ListPendingInvitationsAsync());
            await ThrowsAdministratorRequiredAsync(
                () => disabledAdministrator.CreateInvitationAsync("blocked@example.com"));
            await ThrowsAdministratorRequiredAsync(
                () => disabledAdministrator.RevokeInvitationAsync(Guid.NewGuid()));
        }

        (await administratorA.EnableAccountAsync(harness.OwnerB.Id))
            .Should().Be(AccountChangeResult.Succeeded);
        (await administratorA.ListAccountsAsync())
            .Single(account => account.UserId == harness.OwnerB.Id)
            .IsDisabled.Should().BeFalse();

        await using var verify = fixture.CreateDbContext();
        (await verify.People.AsNoTracking().SingleAsync(person => person.Id == personB))
            .FirstName.Should().Be("Private B profile");
        var enabledOwnerB = await verify.Users.AsNoTracking().SingleAsync(user => user.Id == harness.OwnerB.Id);
        enabledOwnerB.IsDisabled.Should().BeFalse();
    }

    [SqlServerFact]
    public async Task Invitations_are_single_use_capabilities_and_registration_rechecks_instance_rules()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture, makeOwnerAAdministrator: true);
        var invitedEmail = $"invite-{Guid.NewGuid():N}@example.com";
        string invitationToken;
        Guid invitationId;

        await using (var administratorScope = harness.AsOwnerA())
        {
            using var identity = administratorScope.CreateIdentityServices();
            var administrator = CreateAdministrationService(
                administratorScope,
                identity.GetRequiredService<UserManager<RelioUser>>(),
                RegistrationMode.InviteOnly);

            var created = await administrator.CreateInvitationAsync(invitedEmail);
            created.Status.Should().Be(CreateInvitationStatus.Created);
            created.Invitation.Should().NotBeNull();
            invitationId = created.Invitation!.Id;
            invitationToken = created.Invitation.Token;
            invitationToken.Should().NotBeNullOrWhiteSpace();

            var pending = (await administrator.ListPendingInvitationsAsync())
                .Where(invitation => invitation.Id == invitationId)
                .Should().ContainSingle().Which;
            pending.Email.Should().Be(invitedEmail);

            var duplicateAccount = await administrator.CreateInvitationAsync(harness.OwnerB.Email);
            duplicateAccount.Status.Should().Be(CreateInvitationStatus.AlreadyHasAccount);
            (await administrator.CreateInvitationAsync("not-an-email"))
                .Status.Should().Be(CreateInvitationStatus.InvalidEmail);

            var revoked = await administrator.CreateInvitationAsync($"revoke-{Guid.NewGuid():N}@example.com");
            revoked.Status.Should().Be(CreateInvitationStatus.Created);
            var revokedId = revoked.Invitation!.Id;
            (await administrator.RevokeInvitationAsync(revokedId)).Should().BeTrue();
            (await administrator.RevokeInvitationAsync(revokedId)).Should().BeFalse();
            (await administrator.ListPendingInvitationsAsync())
                .Should().NotContain(invitation => invitation.Id == revokedId);

            var openModeAdministration = CreateAdministrationService(
                administratorScope,
                identity.GetRequiredService<UserManager<RelioUser>>(),
                RegistrationMode.Open);
            (await openModeAdministration.CreateInvitationAsync($"unused-{Guid.NewGuid():N}@example.com"))
                .Status.Should().Be(CreateInvitationStatus.InvitationsNotInUse);
        }

        await using (var registrationScope = harness.As(null))
        {
            using var identity = registrationScope.CreateIdentityServices();
            var manager = identity.GetRequiredService<UserManager<RelioUser>>();
            var registration = new AccountRegistrationService(
                registrationScope.DbContext,
                manager,
                new RegistrationLock(),
                Options.Create(new RegistrationOptions
                {
                    Mode = RegistrationMode.InviteOnly,
                    InvitationLifetime = TimeSpan.FromDays(7),
                }),
                harness.Clock,
                NullLogger<AccountRegistrationService>.Instance);

            (await registration.GetEligibilityAsync(null)).Access.Should()
                .Be(RegistrationAccess.RequiresInvitation);
            (await registration.GetEligibilityAsync(invitationToken))
                .Should().Be(new RegistrationEligibility(RegistrationAccess.Allowed, invitedEmail));

            var mismatch = await registration.RegisterAsync(new RegisterAccountRequest(
                $"other-{Guid.NewGuid():N}@example.com",
                SqlIsolationTestHarness.TestPassword,
                "not-a-valid-zone",
                invitationToken));
            mismatch.Status.Should().Be(RegisterAccountStatus.InvitationEmailMismatch);

            var weakPassword = await registration.RegisterAsync(new RegisterAccountRequest(
                invitedEmail,
                "weak",
                "not-a-valid-zone",
                invitationToken));
            weakPassword.Status.Should().Be(RegisterAccountStatus.IdentityErrors);

            var result = await registration.RegisterAsync(new RegisterAccountRequest(
                invitedEmail,
                SqlIsolationTestHarness.TestPassword,
                "Pacific/Kiritimati",
                invitationToken));
            result.Status.Should().Be(RegisterAccountStatus.Succeeded);
            result.UserId.Should().NotBeNullOrWhiteSpace();
            result.IsAdministrator.Should().BeFalse();

            (await registration.GetEligibilityAsync(invitationToken)).Access.Should()
                .Be(RegistrationAccess.InvitationInvalid);
            (await registration.RegisterAsync(new RegisterAccountRequest(
                $"closed-{Guid.NewGuid():N}@example.com",
                SqlIsolationTestHarness.TestPassword,
                "UTC",
                null)))
                .Status.Should().Be(RegisterAccountStatus.InvitationRequired);

            var openRegistration = new AccountRegistrationService(
                registrationScope.DbContext,
                manager,
                new RegistrationLock(),
                Options.Create(new RegistrationOptions { Mode = RegistrationMode.Open }),
                harness.Clock,
                NullLogger<AccountRegistrationService>.Instance);
            (await openRegistration.RegisterAsync(new RegisterAccountRequest(
                $"open-{Guid.NewGuid():N}@example.com",
                SqlIsolationTestHarness.TestPassword,
                "UTC",
                null)))
                .Status.Should().Be(RegisterAccountStatus.Succeeded);
        }

        await using (var verify = fixture.CreateDbContext())
        {
            var registered = await verify.Users.AsNoTracking()
                .SingleAsync(user => user.Email == invitedEmail);
            registered.IsDisabled.Should().BeFalse();
            var profile = await verify.UserProfiles.AsNoTracking()
                .SingleAsync(candidate => candidate.OwnerId == registered.Id);
            profile.TimeZoneId.Should().Be("Pacific/Kiritimati");
            var relationshipTypes = await verify.RelationshipTypes.AsNoTracking()
                .Where(type => type.OwnerId == registered.Id)
                .Select(type => type.Name)
                .ToListAsync();
            relationshipTypes.Should().BeEquivalentTo(RelationshipType.DefaultNames);
            (await verify.RegistrationInvitations.AsNoTracking()
                .AnyAsync(invitation => invitation.Id == invitationId))
                .Should().BeFalse();
        }

        await using (var closedScope = harness.As(null))
        {
            using var identity = closedScope.CreateIdentityServices();
            var registration = new AccountRegistrationService(
                closedScope.DbContext,
                identity.GetRequiredService<UserManager<RelioUser>>(),
                new RegistrationLock(),
                Options.Create(new RegistrationOptions { Mode = RegistrationMode.Closed }),
                harness.Clock,
                NullLogger<AccountRegistrationService>.Instance);

            (await registration.GetEligibilityAsync(null)).Access.Should().Be(RegistrationAccess.Closed);
            var closedEmail = $"closed-refused-{Guid.NewGuid():N}@example.com";
            (await registration.RegisterAsync(new RegisterAccountRequest(
                closedEmail,
                SqlIsolationTestHarness.TestPassword,
                "UTC",
                null))).Status.Should().Be(RegisterAccountStatus.Closed);
            (await identity.GetRequiredService<UserManager<RelioUser>>().FindByEmailAsync(closedEmail))
                .Should().BeNull();
        }
    }

    private static UserAdministrationService CreateAdministrationService(
        SqlIsolationScope scope,
        UserManager<RelioUser> userManager,
        RegistrationMode mode) =>
        new(
            scope.DbContext,
            userManager,
            scope.CurrentUser,
            scope.Clock,
            Options.Create(new RegistrationOptions { Mode = mode }),
            NullLogger<UserAdministrationService>.Instance);

    private static async Task ThrowsAdministratorRequiredAsync(Func<Task> action) =>
        await action.Should().ThrowAsync<AdministratorRequiredException>();
}
