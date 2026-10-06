using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relio.Data.Identity;

namespace Relio.Web.Security;

/// <summary>
/// Periodically re-validates a connected Blazor Server circuit's signed-in user against the
/// database security stamp (issue #16), so a circuit notices when its session stops being valid
/// mid-connection - e.g. the account was signed out of everywhere its password was changed, or
/// its password changed in account settings (#18) or, once #20 ships, its two-factor settings changed. Without this, every
/// <c>AuthenticationStateProvider</c> consumer (<see cref="AuthenticationStateCurrentUser"/>,
/// <c>AuthorizeRouteView</c>, <c>AuthorizeView</c>) only ever sees the principal captured once,
/// when the circuit was created, and a revoked session would stay "signed in" for the rest of the
/// circuit's lifetime no matter how long that is.
/// </summary>
/// <remarks>
/// The standard ASP.NET Core Identity Blazor Web App template shape
/// (<c>IdentityRevalidatingAuthenticationStateProvider</c>), adapted to <see cref="RelioUser"/>.
/// Registered in <c>Program.cs</c> as the <c>AuthenticationStateProvider</c> implementation -
/// <c>AddCascadingAuthenticationState</c>'s cascading parameter and
/// <see cref="AuthenticationStateCurrentUser"/> both resolve whatever is registered as that
/// service, so neither needs to change to pick this up.
/// </remarks>
public sealed class RelioRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityOptions> identityOptions)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    /// <summary>
    /// How often a connected circuit re-checks its user's security stamp. 30 minutes balances
    /// "notices promptly" against cost - short enough that a revoked session does not linger for
    /// the rest of a long-lived circuit, long enough that it is not a meaningful extra load
    /// source (one scoped database read per connected circuit per interval).
    /// </summary>
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        // A fresh scope, not whatever this provider's own (circuit-scoped) services resolved to
        // originally: UserManager/the DbContext behind it must read the current row, not anything
        // already cached for the circuit's lifetime.
        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RelioUser>>();
        return await ValidateSecurityStampAsync(userManager, authenticationState.User);
    }

    private async Task<bool> ValidateSecurityStampAsync(UserManager<RelioUser> userManager, ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return false;
        }

        if (!userManager.SupportsUserSecurityStamp)
        {
            return true;
        }

        var principalStamp = principal.FindFirstValue(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType);
        var userStamp = await userManager.GetSecurityStampAsync(user);
        return SecurityStampsMatch(principalStamp, userStamp);
    }

    /// <summary>
    /// Whether the circuit's cached security stamp claim still matches the user's current one.
    /// Extracted as a pure, public function - no database, no live circuit - so it is unit
    /// testable on its own; the rest of this class is otherwise only exercisable end to end.
    /// </summary>
    public static bool SecurityStampsMatch(string? principalStamp, string? userStamp) =>
        !string.IsNullOrEmpty(userStamp) && string.Equals(principalStamp, userStamp, StringComparison.Ordinal);
}
