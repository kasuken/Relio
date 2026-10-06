using Microsoft.AspNetCore.Identity;

namespace Relio.Data.Identity;

/// <summary>
/// The two state changes that make up "turning two-factor authentication on" and "turning it off"
/// (issue #20), kept in one place so the pages and the tests agree on exactly what each one does.
/// </summary>
/// <remarks>
/// Both rotate the user's security stamp (<c>SetTwoFactorEnabledAsync</c> and
/// <c>ResetAuthenticatorKeyAsync</c> do), which signs out every <i>other</i> session at its next
/// validation. The caller must call <c>SignInManager.RefreshSignInAsync</c> afterwards or the user
/// who just made the change would be signed out too. Neither method checks the current password or
/// an authenticator code - that is the page's job, before it calls these.
/// </remarks>
public static class TwoFactorAccountExtensions
{
    /// <summary>How many recovery codes are issued whenever two-factor authentication is turned on or regenerated.</summary>
    public const int RecoveryCodeCount = 10;

    /// <summary>
    /// Turns two-factor authentication on and replaces whatever recovery codes the account had with
    /// <see cref="RecoveryCodeCount"/> fresh ones. The authenticator key must already exist
    /// (<c>ResetAuthenticatorKeyAsync</c> on first setup) and the code the user typed must already
    /// have been verified against it.
    /// </summary>
    /// <returns>
    /// The new recovery codes, to be shown to the user exactly once, or <see langword="null"/> when
    /// something failed (nothing is then guaranteed about the account's state; the caller should show
    /// a generic error and let the user try again).
    /// </returns>
    public static async Task<IReadOnlyList<string>?> TurnOnTwoFactorAsync(this UserManager<RelioUser> userManager, RelioUser user)
    {
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(user);

        var enabled = await userManager.SetTwoFactorEnabledAsync(user, true);
        if (!enabled.Succeeded)
        {
            return null;
        }

        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);
        return codes?.ToList();
    }

    /// <summary>
    /// Turns two-factor authentication off, replaces the authenticator key (so the codes from the
    /// old app can never work again, even if two-factor authentication is later turned back on) and
    /// clears every recovery code.
    /// </summary>
    /// <returns>The first failing <see cref="IdentityResult"/>, or a success.</returns>
    public static async Task<IdentityResult> TurnOffTwoFactorAsync(this UserManager<RelioUser> userManager, RelioUser user)
    {
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(user);

        var disabled = await userManager.SetTwoFactorEnabledAsync(user, false);
        if (!disabled.Succeeded)
        {
            return disabled;
        }

        var reset = await userManager.ResetAuthenticatorKeyAsync(user);
        if (!reset.Succeeded)
        {
            return reset;
        }

        // Asking for zero codes stores an empty list: the supported way to clear them.
        var cleared = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
        return cleared is null
            ? IdentityResult.Failed(new IdentityError { Code = "RecoveryCodesNotCleared", Description = "Recovery codes could not be cleared." })
            : IdentityResult.Success;
    }
}
