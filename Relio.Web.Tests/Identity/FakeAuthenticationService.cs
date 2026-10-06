using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace Relio.Web.Tests.Identity;

/// <summary>
/// An <see cref="IAuthenticationService"/> that keeps "cookies" in memory instead of writing
/// headers, so a test can drive Identity's two-factor sign-in across several requests (scopes): the
/// password step in one, the code step in another, exactly as the app does. It records every
/// sign-in and sign-out per scheme so tests can assert what Identity asked for.
/// </summary>
/// <remarks>
/// Only the two-factor user id cookie is "persisted" (its principal comes back from
/// <see cref="AuthenticateAsync"/>); every other scheme authenticates as nobody.
/// </remarks>
internal sealed class FakeAuthenticationService : IAuthenticationService
{
    private ClaimsPrincipal? _pendingTwoFactor;

    public List<(string? Scheme, ClaimsPrincipal Principal)> SignIns { get; } = [];

    public List<string?> SignOuts { get; } = [];

    public bool HasPendingTwoFactorStep => _pendingTwoFactor is not null;

    public IEnumerable<ClaimsPrincipal> ApplicationSignIns =>
        SignIns.Where(s => s.Scheme == IdentityConstants.ApplicationScheme).Select(s => s.Principal);

    public bool SignedInWith(string scheme) => SignIns.Any(s => s.Scheme == scheme);

    public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
    {
        if (scheme == IdentityConstants.TwoFactorUserIdScheme && _pendingTwoFactor is not null)
        {
            var ticket = new AuthenticationTicket(_pendingTwoFactor, scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(AuthenticateResult.NoResult());
    }

    public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
    {
        SignIns.Add((scheme, principal));
        if (scheme == IdentityConstants.TwoFactorUserIdScheme)
        {
            _pendingTwoFactor = principal;
        }

        return Task.CompletedTask;
    }

    public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
    {
        SignOuts.Add(scheme);
        if (scheme == IdentityConstants.TwoFactorUserIdScheme)
        {
            _pendingTwoFactor = null;
        }

        return Task.CompletedTask;
    }

    public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

    public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
}
