namespace Relio.Application.Reminders;

/// <summary>
/// Abstraction for delivering reminders by email (Issue #40).
/// Reminder emails contain ONLY the person's name and reminder title (and due date),
/// plus the unsubscribe link. No notes or other personal details are ever included.
/// </summary>
public interface IReminderEmailSender
{
    /// <summary>
    /// Sends an immediate reminder email for a single reminder.
    /// </summary>
    Task SendImmediateReminderAsync(
        string toEmail,
        string personName,
        string reminderTitle,
        DateOnly dueDate,
        string? unsubscribeToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a daily digest email summarising all reminders due for the day.
    /// </summary>
    Task SendDailyDigestAsync(
        string toEmail,
        IReadOnlyList<DigestReminderItem> items,
        string? unsubscribeToken,
        CancellationToken cancellationToken = default);
}
