namespace Relio.Application.Accounts;

/// <summary>
/// Permanently removes the signed-in user's account and all account-owned data after a current
/// password check and explicit confirmation.
/// </summary>
public interface IAccountDeletionService
{
    /// <summary>Deletes the current account after reauthentication and confirmation.</summary>
    /// <param name="request">The current password and explicit deletion confirmation.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A safe outcome; no account identifier or private content is returned.</returns>
    Task<AccountDeletionResult> DeleteAsync(
        AccountDeletionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>The inputs required to erase the current account.</summary>
/// <param name="CurrentPassword">The current local-account password, never logged or persisted.</param>
/// <param name="Confirmed">Whether the user explicitly confirmed permanent deletion.</param>
public sealed record AccountDeletionRequest(string CurrentPassword, bool Confirmed);

/// <summary>Safe outcomes of a current-account erasure request.</summary>
public enum AccountDeletionStatus
{
    /// <summary>The account and its data were deleted in one database save.</summary>
    Deleted,

    /// <summary>The explicit confirmation was not supplied.</summary>
    ConfirmationRequired,

    /// <summary>The supplied current password did not verify.</summary>
    CurrentPasswordIncorrect,

    /// <summary>The signed-in account no longer exists or is disabled.</summary>
    AccountUnavailable,

    /// <summary>Deleting this account would leave no active Administrator.</summary>
    LastActiveAdministrator,

    /// <summary>A concurrent account or owned-data change prevented a safe deletion.</summary>
    ConcurrentChange,
}

/// <summary>The result of an account-erasure request.</summary>
/// <param name="Status">The outcome, without exposing database or identity details.</param>
/// <param name="ConfirmationAddress">
/// The account's email address only when deletion succeeded, for an optional post-commit
/// confirmation email. The application must not log or persist this value.
/// </param>
public sealed record AccountDeletionResult(
    AccountDeletionStatus Status,
    string? ConfirmationAddress = null);
