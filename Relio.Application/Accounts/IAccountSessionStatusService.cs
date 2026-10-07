namespace Relio.Application.Accounts;

/// <summary>
/// Checks whether the current signed-in account still exists and is allowed to use this instance.
/// Used at circuit and request boundaries; it is not an owned-data access bypass.
/// </summary>
public interface IAccountSessionStatusService
{
    /// <summary>Reads the current account's fresh status without trusting a long-lived tracked copy.</summary>
    /// <param name="cancellationToken">Cancels the database read.</param>
    /// <returns>Whether the account exists and is enabled.</returns>
    Task<AccountSessionStatus> GetCurrentStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>Fresh account status for an authenticated session.</summary>
public enum AccountSessionStatus
{
    /// <summary>The current account exists and is enabled.</summary>
    Active,

    /// <summary>The account no longer exists.</summary>
    Missing,

    /// <summary>The account exists but an Administrator disabled it.</summary>
    Disabled,
}
