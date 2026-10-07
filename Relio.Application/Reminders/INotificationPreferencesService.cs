namespace Relio.Application.Reminders;

/// <summary>
/// Service managing notification and reminder email preferences for the authenticated user (Issue #40).
/// </summary>
public interface INotificationPreferencesService
{
    /// <summary>
    /// Gets the current user's notification preferences, generating an unsubscribe token if missing.
    /// </summary>
    Task<NotificationPreferencesDto> GetPreferencesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the current user's notification preferences.
    /// </summary>
    Task SetPreferencesAsync(UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken = default);
}
