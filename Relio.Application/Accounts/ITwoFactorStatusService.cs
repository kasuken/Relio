namespace Relio.Application.Accounts;

/// <summary>
/// Read-only view of the signed-in user's two-factor authentication state (issue #20), for the
/// "Sign-in and security" section of account settings. Changing it is deliberately not part of
/// this interface: turning two-factor authentication on or off, or issuing recovery codes, has to
/// write the sign-in cookie (the security stamp rotates) and so lives in the static SSR account
/// pages under <c>/Account/Manage</c> - see "Accounts and authentication" in AGENTS.md.
/// </summary>
/// <remarks>
/// Scoped to the signed-in user (<see cref="Security.ICurrentUser"/>), like every other Application
/// service. The answer is always read fresh from the database, never from a tracked entity, because
/// an interactive Blazor circuit keeps one long-lived <c>DbContext</c> that would otherwise keep
/// showing the state from when the circuit started.
/// </remarks>
public interface ITwoFactorStatusService
{
    /// <summary>The current user's two-factor state.</summary>
    /// <exception cref="Security.UnauthenticatedUserException">Nobody is signed in.</exception>
    Task<TwoFactorStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Whether two-factor authentication is on for an account and how many unused recovery codes it
/// still has.
/// </summary>
/// <param name="IsEnabled">Whether signing in asks for a code from an authenticator app.</param>
/// <param name="RecoveryCodesLeft">How many unused recovery codes the account has.</param>
public sealed record TwoFactorStatus(bool IsEnabled, int RecoveryCodesLeft)
{
    /// <summary>
    /// At or below this many remaining recovery codes the UI nudges the user to generate new ones
    /// (only while two-factor authentication is on).
    /// </summary>
    public const int LowRecoveryCodeThreshold = 3;

    /// <summary>Whether to warn the user that they are running out of recovery codes.</summary>
    public bool RecoveryCodesLow => IsEnabled && RecoveryCodesLeft <= LowRecoveryCodeThreshold;
}
