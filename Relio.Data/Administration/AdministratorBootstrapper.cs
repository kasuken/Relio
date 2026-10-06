using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relio.Application.Administration;
using Relio.Data.Identity;

namespace Relio.Data.Administration;

/// <summary>
/// Startup housekeeping for self-hosted administration (issue #19): promotes the account named by
/// <c>Administration:AdministratorEmail</c>, purges expired invitations, and warns when an instance
/// has accounts but no Administrator. Runs once at startup (see <c>Program.cs</c>), never from a
/// request, and is idempotent.
/// </summary>
/// <remarks>
/// This is how an instance that was running before issue #19 gets its first Administrator (its
/// accounts predate the "first account becomes Administrator" rule, which deliberately does not
/// hand the role to whoever registers next), and how an operator recovers if nobody is one. A role
/// is read into the sign-in cookie when someone signs in, so a promoted account needs a fresh
/// sign-in before the Administration link appears. Logs ids only - never the configured address.
/// </remarks>
public sealed class AdministratorBootstrapper(
    RelioDbContext dbContext,
    UserManager<RelioUser> userManager,
    IOptions<AdministrationOptions> options,
    TimeProvider timeProvider,
    ILogger<AdministratorBootstrapper> logger)
{
    /// <summary>Runs the startup checks.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await PurgeExpiredInvitationsAsync(cancellationToken);

        var email = options.Value.AdministratorEmail;
        if (!string.IsNullOrWhiteSpace(email))
        {
            var user = await userManager.FindByEmailAsync(email.Trim());
            if (user is null)
            {
                logger.LogWarning(
                    "Administration:AdministratorEmail is set but no account has that address, so nobody was promoted.");
            }
            else if (!await userManager.IsInRoleAsync(user, RelioRoles.Administrator))
            {
                var result = await userManager.AddToRoleAsync(user, RelioRoles.Administrator);
                if (result.Succeeded)
                {
                    logger.LogInformation(
                        "Account {UserId} was made Administrator through Administration:AdministratorEmail.", user.Id);
                }
                else
                {
                    logger.LogError(
                        "Could not make account {UserId} Administrator: {Errors}",
                        user.Id,
                        string.Join("; ", result.Errors.Select(e => e.Code)));
                }
            }
        }

        if (await dbContext.Users.AnyAsync(cancellationToken)
            && (await userManager.GetUsersInRoleAsync(RelioRoles.Administrator)).Count == 0)
        {
            logger.LogWarning(
                "This instance has accounts but no Administrator, so nobody can manage accounts or invitations. " +
                "Set Administration:AdministratorEmail to the email address of the account that should be the " +
                "Administrator and restart.");
        }
    }

    private async Task PurgeExpiredInvitationsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expired = await dbContext.RegistrationInvitations
            .Where(i => i.ExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);
        if (expired.Count == 0)
        {
            return;
        }

        dbContext.RegistrationInvitations.RemoveRange(expired);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
