using Relio.Domain;

namespace Relio.Application.Reminders;

/// <summary>
/// User notification preferences data transfer object (Issue #40).
/// </summary>
public sealed record NotificationPreferencesDto(
    ReminderEmailDelivery Delivery,
    bool BirthdayRemindersEnabled,
    int DefaultBirthdayLeadDays,
    string? UnsubscribeToken);
