namespace Relio.Domain;

/// <summary>
/// How the user receives reminder emails.
/// </summary>
public enum ReminderEmailDelivery
{
    /// <summary>Email reminders are disabled. Reminders appear on the dashboard only.</summary>
    None = 0,

    /// <summary>Send an email for each reminder as it becomes due.</summary>
    Immediate = 1,

    /// <summary>Send a daily digest email summarising all reminders due for the day.</summary>
    DailyDigest = 2,
}
