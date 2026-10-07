namespace Relio.Web.Email;

/// <summary>Sends the minimal post-commit notice for a completed account deletion.</summary>
public interface IAccountDeletionConfirmationSender
{
    /// <summary>Whether this instance has an email provider configured.</summary>
    bool IsAvailable { get; }

    /// <summary>Sends a confirmation without account names, private content, or links.</summary>
    /// <param name="address">The deleted account's address, held only for this delivery.</param>
    /// <param name="cancellationToken">Cancels email delivery.</param>
    Task SendAsync(string address, CancellationToken cancellationToken = default);
}
