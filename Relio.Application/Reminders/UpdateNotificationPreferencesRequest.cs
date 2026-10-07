using Relio.Domain;

namespace Relio.Application.Reminders;

/// <summary>
/// Request to update notification preferences (Issue #40).
/// </summary>
public sealed record UpdateNotificationPreferencesRequest(
    ReminderEmailDelivery Delivery,
    bool BirthdayRemindersEnabled,
    int DefaultBirthdayLeadDays);
