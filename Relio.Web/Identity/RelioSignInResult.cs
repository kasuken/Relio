using Microsoft.AspNetCore.Identity;

namespace Relio.Web.Identity;

/// <summary>
/// A <see cref="SignInResult"/> that can also say "this account is disabled" (issue #19). It is
/// still <see cref="SignInResult.IsNotAllowed"/>, so any code that only knows Identity's result
/// treats it as a refused sign-in; <c>Login.razor</c> checks <see cref="IsDisabled"/> first to show
/// the right message.
/// </summary>
/// <remarks>
/// Only <see cref="RelioSignInManager"/> returns it, and only after the password was verified, so
/// the result never reveals whether a disabled account exists to someone who does not know its
/// password.
/// </remarks>
public sealed class RelioSignInResult : SignInResult
{
    /// <summary>The result for a correct password on a disabled account.</summary>
    public static RelioSignInResult Disabled { get; } = new() { IsNotAllowed = true, IsDisabled = true };

    /// <summary>Whether an Administrator disabled the account.</summary>
    public bool IsDisabled { get; private init; }

    /// <inheritdoc />
    public override string ToString() => IsDisabled ? "Disabled" : base.ToString();
}
