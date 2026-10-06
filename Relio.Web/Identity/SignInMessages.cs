namespace Relio.Web.Identity;

/// <summary>
/// The user-facing messages of the sign-in pages that more than one page shows: the password step
/// (<c>Login.razor</c>) and both two-factor steps (<c>LoginWith2fa.razor</c>,
/// <c>LoginWithRecoveryCode.razor</c>, issue #20). One definition, so the steps can never drift into
/// answering the same situation in different words - which would let an observer tell them apart.
/// </summary>
public static class SignInMessages
{
    /// <summary>
    /// Deliberately the same message whether the email does not exist, the password is wrong, or an
    /// unlocked account's password check fails - never let the login form distinguish "no such
    /// account" from "wrong password" (no account enumeration).
    /// </summary>
    public const string InvalidCredentials = "Email or password is incorrect.";

    /// <summary>
    /// Deliberately vague about exactly when the lockout lifts or how many attempts were made -
    /// just enough for a genuine user to understand what happened and that trying again shortly
    /// will work, without handing an attacker a precise timer or attempt-count oracle. Shown by the
    /// password step and both two-factor steps: they share one lockout budget.
    /// </summary>
    public const string LockedOut =
        "Too many failed sign-in attempts. Your account is temporarily locked - please try again in a few minutes.";

    /// <summary>
    /// Shown when the password (or, at the second step, the account) was verified but an
    /// Administrator disabled the account (issue #19). Only ever reachable after the correct
    /// password (see <see cref="RelioSignInManager"/>), so it reveals nothing to someone guessing
    /// at accounts.
    /// </summary>
    public const string Disabled =
        "This account has been disabled. If you think that's a mistake, contact the person who runs this Relio instance.";
}
