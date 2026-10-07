namespace Relio.Web.Email;

/// <summary>Represents an instance configured without an email provider.</summary>
public sealed class NoAccountDeletionConfirmationSender : IAccountDeletionConfirmationSender
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task SendAsync(string address, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
