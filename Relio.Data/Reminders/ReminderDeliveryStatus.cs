namespace Relio.Data.Reminders;

/// <summary>Where a <see cref="ReminderDelivery"/> claim stands.</summary>
public enum ReminderDeliveryStatus
{
    /// <summary>Claimed by a worker that has not yet recorded the outcome. Never re-sent.</summary>
    Pending = 0,

    /// <summary>The email was handed to the sender.</summary>
    Sent = 1,
}
