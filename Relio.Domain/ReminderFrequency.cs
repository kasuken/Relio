namespace Relio.Domain;

/// <summary>
/// How often a reconnect reminder repeats.
/// </summary>
public enum ReminderFrequency
{
    /// <summary>Fires once and is then marked complete.</summary>
    Once = 0,

    /// <summary>Repeats every week (7 days).</summary>
    Weekly = 1,

    /// <summary>Repeats every month.</summary>
    Monthly = 2,

    /// <summary>Repeats every 3 months (quarterly).</summary>
    EveryThreeMonths = 3,

    /// <summary>Repeats every 6 months.</summary>
    EverySixMonths = 4,

    /// <summary>Repeats every year.</summary>
    Yearly = 5,

    /// <summary>Repeats every N months, specified by <see cref="Reminder.CustomIntervalMonths"/>.</summary>
    CustomMonths = 6,
}
