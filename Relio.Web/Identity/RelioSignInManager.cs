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
/// <para>
/// <b>Two-factor sign-in (issue #20).</b> Identity's two-factor methods work from the
/// <c>Identity.TwoFactorUserId</c> cookie, which is valid for five minutes after the password check.
/// Two gaps in the base class are closed here:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Disabled during the code step.</b> <see cref="SignInOrTwoFactorAsync"/> above stops a disabled
/// account before that cookie is issued, but an Administrator can disable an account <i>between</i>
/// the password and the code. The base two-factor methods only ask <c>CanSignInAsync</c>
/// (confirmed email), which this class deliberately does not override (that hook runs before the
/// password check and would turn the form into an account-existence oracle), so each two-factor method
/// re-checks <see cref="RelioUser.IsDisabled"/>, clears the cookie and answers
/// <see cref="RelioSignInResult.Disabled"/>. (The final <see cref="SignInWithClaimsAsync(RelioUser, AuthenticationProperties?, IEnumerable{Claim})"/>
/// backstop would also refuse the cookie, but would report a plain success.)
/// </item>
/// <item>
/// <b>Recovery codes had no lockout.</b> <c>TwoFactorRecoveryCodeSignInAsync</c> skips
/// <c>PreSignInCheck</c> (so a locked-out account could still sign in with a valid code) and never
/// counts a wrong code, because Identity assumes codes are random enough not to guess. They are
/// (about 50 bits), but "an attacker with the password gets unlimited guesses" is a poor property
/// when the fix is five lines: the override runs <c>PreSignInCheck</c> first and counts a wrong code
/// against the same <c>Account:Lockout</c> budget as a wrong password or authenticator code.
/// </item>
/// </list>
/// <para>
/// A correct password does not reset the failed-attempt counter for an account with two-factor
/// authentication (Identity resets it only once the whole sign-in completes), so password, code and
/// recovery-code failures all add up towards one lockout.
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
    public override async Task<SignInResult> TwoFactorAuthenticatorSignInAsync(
        string code, bool isPersistent, bool rememberClient)
    {
        return await RefuseDisabledTwoFactorUserAsync()
            ?? await base.TwoFactorAuthenticatorSignInAsync(code, isPersistent, rememberClient);
    }

    /// <inheritdoc />
    public override async Task<SignInResult> TwoFactorSignInAsync(
        string provider, string code, bool isPersistent, bool rememberClient)
    {
        return await RefuseDisabledTwoFactorUserAsync()
            ?? await base.TwoFactorSignInAsync(provider, code, isPersistent, rememberClient);
    }

    /// <inheritdoc />
    public override async Task<SignInResult> TwoFactorRecoveryCodeSignInAsync(string recoveryCode)
    {
        var user = await GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return SignInResult.Failed;
        }

        if (await RefuseDisabledTwoFactorUserAsync() is { } disabled)
        {
            return disabled;
        }

        // What the base method skips: not-confirmed and locked-out accounts are refused before a
        // code is even looked at.
        var error = await PreSignInCheck(user);
        if (error is not null)
        {
            return error;
        }

        var result = await base.TwoFactorRecoveryCodeSignInAsync(recoveryCode);
        if (result.Succeeded)
        {
            return result;
        }

        // Count the wrong code like a wrong password or authenticator code, so one budget covers
        // every guess an attacker who knows the password can make.
        if (UserManager.SupportsUserLockout)
        {
            var increment = await UserManager.AccessFailedAsync(user) ?? IdentityResult.Success;
            if (increment.Succeeded && await UserManager.IsLockedOutAsync(user))
            {
                return await LockedOut(user);
            }
        }

        return SignInResult.Failed;
    }

    /// <summary>
    /// When the account waiting at the two-factor step has been disabled since its password was
    /// checked, ends that step (clears the two-factor cookie) and answers
    /// <see cref="RelioSignInResult.Disabled"/>; otherwise <see langword="null"/>.
    /// </summary>
    private async Task<SignInResult?> RefuseDisabledTwoFactorUserAsync()
    {
        var user = await GetTwoFactorAuthenticationUserAsync();
        if (user is not { IsDisabled: true })
        {
            return null;
        }

        Logger.LogWarning("Refused the two-factor step for disabled account {UserId}.", user.Id);
        await Context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        return RelioSignInResult.Disabled;
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
