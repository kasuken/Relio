namespace Relio.Application.Reminders;

/// <summary>
/// Service handling one-click email unsubscriptions via secure tokens (Issue #40).
/// Does not require the user to be signed in.
/// </summary>
public interface IUnsubscribeService
{
    /// <summary>
    /// Unsubscribes the user matching the given token by setting <c>ReminderEmailDelivery</c> to <c>None</c>.
    /// Returns true if a user profile matching the token was found and updated; otherwise false.
    /// </summary>
    Task<bool> UnsubscribeAsync(string token, CancellationToken cancellationToken = default);
}
