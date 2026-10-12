namespace Relio.Data.Reminders;

/// <summary>What a <see cref="ReminderDelivery"/> row delivered a notification for.</summary>
public enum ReminderDeliveryKind
{
    /// <summary>A persisted <c>Reminder</c>; the source id is the reminder id.</summary>
    Reminder = 0,

    /// <summary>A birthday calculated from a person; the source id is the person id.</summary>
    Birthday = 1,
}
