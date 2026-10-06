using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Relio.Data.Identity;

namespace Relio.Web.Identity;

/// <summary>
/// <see cref="SignInManager{TUser}"/> that refuses disabled accounts (issue #19) without turning
/// the sign-in form into an oracle for which accounts exist.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the check goes, and why.</b> <c>PasswordSignInAsync</c> verifies the password in a
/// private method, so overriding <c>CheckPasswordSignInAsync</c> has no effect on it, and
/// overriding <c>CanSignInAsync</c> would run <i>before</i> the password is checked - answering
/// "this account is disabled" to anyone who types its email, with or without the password. The one
/// protected hook reached only after a successful password check (and before two-factor, #20) is
/// <see cref="SignInOrTwoFactorAsync"/>. A wrong password therefore gets the usual "Email or
/// password is incorrect" and counts towards lockout exactly as before; a correct one on a disabled
/// account gets <see cref="RelioSignInResult.Disabled"/>.
/// </para>
/// <para>
/// <b>Ending sessions.</b> <see cref="ValidateSecurityStampAsync(ClaimsPrincipal)"/> is what the
/// cookie's security stamp validator calls, so a disabled user's existing cookies are rejected at
/// the next validation (<c>Account:Session:ValidationInterval</c>). <see cref="SignInWithClaimsAsync(RelioUser, AuthenticationProperties?, IEnumerable{Claim})"/>
/// is a backstop: whatever path leads to it, a disabled account never receives a cookie.
/// </para>
/// </remarks>
public sealed class RelioSignInManager(
    UserManager<RelioUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<RelioUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<RelioUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<RelioUser> confirmation)
    : SignInManager<RelioUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    /// <inheritdoc />
    protected override async Task<SignInResult> SignInOrTwoFactorAsync(
        RelioUser user, bool isPersistent, string? loginProvider = null, bool bypassTwoFactor = false)
    {
        if (user.IsDisabled)
        {
            return RelioSignInResult.Disabled;
        }

        return await base.SignInOrTwoFactorAsync(user, isPersistent, loginProvider, bypassTwoFactor);
    }

    /// <inheritdoc />
    public override async Task<RelioUser?> ValidateSecurityStampAsync(ClaimsPrincipal? principal)
    {
        var user = await base.ValidateSecurityStampAsync(principal);
        return user is { IsDisabled: true } ? null : user;
    }

    /// <inheritdoc />
    public override Task SignInWithClaimsAsync(
        RelioUser user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        if (user.IsDisabled)
        {
            Logger.LogWarning("Refused to issue a sign-in cookie to disabled account {UserId}.", user.Id);
            return Task.CompletedTask;
        }

        return base.SignInWithClaimsAsync(user, authenticationProperties, additionalClaims);
    }
}
