using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relio.Application.Administration;
using Relio.Application.Time;
using Relio.Data.Identity;
using Relio.Domain;

namespace Relio.Data.Administration;

/// <summary>
/// EF Core and Identity backed implementation of <see cref="IAccountRegistrationService"/>
/// (issue #19). Lives in Relio.Data because it depends on <see cref="RelioDbContext"/> and
/// <see cref="UserManager{TUser}"/> directly, like <c>Relio.Data.Seeding.DemoDataSeeder</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The first-account rule.</b> A new account becomes the instance's Administrator only when it
/// is the <i>only</i> account in the database right after its own insert - not merely "no
/// Administrator exists yet", which would let a stranger who finds an instance that predates
/// issue #19 (accounts, no Administrator) claim it by registering. Instances that predate the rule
/// get their Administrator from <c>Administration:AdministratorEmail</c> instead (see
/// <see cref="AdministratorBootstrapper"/>).
/// </para>
/// <para>
/// <b>Why two accounts can not both become the Administrator.</b> Two layers. (1) The singleton
/// <see cref="RegistrationLock"/> serializes registrations within a process, so the second one only
/// ever starts after the first one has saved and therefore never sees an empty instance. (2) For
/// more than one process sharing a database, the decision is made <i>after</i> the insert:
/// <c>wasEmpty &amp;&amp; CountAsync() == 1</c>. If two processes both see an empty instance and
/// both insert, each then counts at least 2 accounts, so neither is promoted. The worst case is an
/// instance with no Administrator, which is recoverable (and logged) through
/// <c>Administration:AdministratorEmail</c>; it can never be an instance with two accidental
/// ones. On a non-open instance that same race is resolved by deleting the account that lost it,
/// so a closed or invitation-only instance can not end up with an uninvited extra account.
/// </para>
/// <para>
/// Nothing here logs an email address, a token or a link - only user ids (see the gdpr-compliant
/// skill).
/// </para>
/// </remarks>
public sealed class AccountRegistrationService(
    RelioDbContext dbContext,
    UserManager<RelioUser> userManager,
    RegistrationLock registrationLock,
    IOptions<RegistrationOptions> options,
    TimeProvider timeProvider,
    ILogger<AccountRegistrationService> logger) : IAccountRegistrationService
{
    /// <inheritdoc />
    public async Task<RegistrationEligibility> GetEligibilityAsync(
        string? invitationToken, CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Users.AnyAsync(cancellationToken))
        {
            return new RegistrationEligibility(RegistrationAccess.FirstAccount);
        }

        switch (options.Value.Mode)
        {
            case RegistrationMode.Closed:
                return new RegistrationEligibility(RegistrationAccess.Closed);

            case RegistrationMode.InviteOnly:
                if (string.IsNullOrWhiteSpace(invitationToken))
                {
                    return new RegistrationEligibility(RegistrationAccess.RequiresInvitation);
                }

                var invitation = await FindPendingInvitationAsync(invitationToken, cancellationToken);
                return invitation is null
                    ? new RegistrationEligibility(RegistrationAccess.InvitationInvalid)
                    : new RegistrationEligibility(RegistrationAccess.Allowed, invitation.Email);

            default:
                // Open: an invitation token, if one was supplied, is simply ignored.
                return new RegistrationEligibility(RegistrationAccess.Allowed);
        }
    }

    /// <inheritdoc />
    public async Task<RegisterAccountResult> RegisterAsync(
        RegisterAccountRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Everything below, including the "is the instance empty?" read, happens inside the lock -
        // see the remarks for why the order matters.
        using var _ = await registrationLock.AcquireAsync(cancellationToken);

        var mode = options.Value.Mode;
        var wasEmpty = !await dbContext.Users.AnyAsync(cancellationToken);

        RegistrationInvitation? invitation = null;
        if (!wasEmpty)
        {
            // Re-check the instance's rules here, not only in GetEligibilityAsync: a crafted POST
            // never goes through the page that decided whether to show the form.
            switch (mode)
            {
                case RegistrationMode.Closed:
                    return RegisterAccountResult.Refused(RegisterAccountStatus.Closed);

                case RegistrationMode.InviteOnly:
                    if (string.IsNullOrWhiteSpace(request.InvitationToken))
                    {
                        return RegisterAccountResult.Refused(RegisterAccountStatus.InvitationRequired);
                    }

                    invitation = await FindPendingInvitationAsync(request.InvitationToken, cancellationToken);
                    if (invitation is null)
                    {
                        return RegisterAccountResult.Refused(RegisterAccountStatus.InvitationInvalid);
                    }

                    if (!string.Equals(
                            userManager.NormalizeEmail(request.Email), invitation.NormalizedEmail, StringComparison.Ordinal))
                    {
                        // The invitation stays usable: a typo in the address must not burn it.
                        return RegisterAccountResult.Refused(RegisterAccountStatus.InvitationEmailMismatch);
                    }

                    break;
            }
        }

        var user = new RelioUser { UserName = request.Email, Email = request.Email };
        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            // Nothing was created, so nothing is consumed: an invitation can be retried with a
            // stronger password.
            return RegisterAccountResult.Failed(
                createResult.Errors.Select(e => new RegistrationError(e.Code, e.Description)).ToList());
        }

        // Decided after the insert, so "I was alone" is verified, not assumed (see the remarks).
        var isAdministrator = wasEmpty && await dbContext.Users.CountAsync(cancellationToken) == 1;

        if (wasEmpty && !isAdministrator && mode != RegistrationMode.Open)
        {
            // Another process registered at the same moment on an instance that does not take open
            // sign-ups; this account did not win the race to be the first, so it must not exist.
            await userManager.DeleteAsync(user);
            logger.LogWarning(
                "Registration lost a first-account race on a {RegistrationMode} instance; the account {UserId} was removed.",
                mode,
                user.Id);

            return RegisterAccountResult.Refused(
                mode == RegistrationMode.Closed ? RegisterAccountStatus.Closed : RegisterAccountStatus.InvitationRequired);
        }

        if (isAdministrator)
        {
            var roleResult = await userManager.AddToRoleAsync(user, RelioRoles.Administrator);
            if (!roleResult.Succeeded)
            {
                // Recoverable (Administration:AdministratorEmail), and not worth failing a sign-up
                // that already created the account.
                logger.LogError(
                    "Could not make the first account {UserId} an Administrator: {Errors}",
                    user.Id,
                    string.Join("; ", roleResult.Errors.Select(e => e.Code)));
                isAdministrator = false;
            }
        }

        if (invitation is not null)
        {
            dbContext.RegistrationInvitations.Remove(invitation);
        }

        // No ICurrentUser yet: the new user is not signed in during the request that creates them,
        // so this is the one place a user's own profile row and starting data are written without
        // one - the same escape hatch DemoDataSeeder uses at startup. It lives here (not in
        // Register.razor) so the rule is in one tested place. The default relationship types are
        // seeded here, in the same save as the profile, and nowhere lazily: a default the user
        // later deletes (issue #25) must not come back. Every path that creates a RelioUser does
        // the same (see the "User-scoped data pattern" section of AGENTS.md).
        var timeZoneId = TimeZoneIds.TryParse(request.TimeZoneId, out var parsedTimeZone)
            ? parsedTimeZone.Id
            : TimeZoneIds.Default;
        dbContext.UserProfiles.Add(new UserProfile
        {
            OwnerId = user.Id,
            TimeZoneId = timeZoneId,
            OnboardingDismissed = false,
        });
        dbContext.RelationshipTypes.AddRange(RelationshipType.CreateDefaults(user.Id));
        await dbContext.SaveChangesAsync(cancellationToken);

        if (wasEmpty && isAdministrator)
        {
            logger.LogInformation(
                "Account {UserId} registered as the instance's first account and made Administrator.", user.Id);
        }
        else
        {
            logger.LogInformation("Account {UserId} registered.", user.Id);
        }

        return RegisterAccountResult.Success(user.Id, isAdministrator);
    }

    private async Task<RegistrationInvitation?> FindPendingInvitationAsync(
        string token, CancellationToken cancellationToken)
    {
        var hash = InvitationTokens.Hash(token.Trim());
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return await dbContext.RegistrationInvitations
            .FirstOrDefaultAsync(i => i.TokenHash == hash && i.ExpiresAtUtc > now, cancellationToken);
    }
}
