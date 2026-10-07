namespace Relio.Domain;

/// <summary>
/// A reminder to reconnect with a person (epic #36, issue #37).
/// </summary>
public sealed class Reminder : OwnedEntity
{
    /// <summary>The longest reminder <see cref="Title"/> Relio stores, in characters.</summary>
    public const int TitleMaxLength = 200;

    /// <summary>The person this reminder belongs to.</summary>
    public Guid PersonId { get; set; }

    /// <summary>The person this reminder belongs to, when loaded.</summary>
    public Person? Person { get; set; }

    /// <summary>What the user wants to remember or do (e.g. "Catch up over coffee").</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The scheduled due date in the user's calendar.</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>How often the reminder recurs, or <see cref="ReminderFrequency.Once"/> for a one-off.</summary>
    public ReminderFrequency Frequency { get; set; } = ReminderFrequency.Once;

    /// <summary>The interval in months when <see cref="Frequency"/> is <see cref="ReminderFrequency.CustomMonths"/>.</summary>
    public int? CustomIntervalMonths { get; set; }

    /// <summary>When set, the reminder is snoozed until this date instead of <see cref="DueDate"/>.</summary>
    public DateOnly? SnoozedUntilDate { get; set; }

    /// <summary>Whether the reminder has been completed.</summary>
    public bool IsCompleted { get; set; }

    /// <summary>When the reminder was completed (UTC).</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>
    /// The user-calendar date when this reminder was last delivered to the user (e.g. by email),
    /// guaranteeing idempotent delivery (issue #39).
    /// </summary>
    public DateOnly? LastDeliveredDate { get; set; }

    /// <summary>
    /// The active date for display, sorting and due checks: the snooze date when snoozed,
    /// otherwise <see cref="DueDate"/>. Not mapped by EF Core.
    /// </summary>
    public DateOnly EffectiveDueDate => SnoozedUntilDate ?? DueDate;
}
