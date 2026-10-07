using Relio.Domain;

namespace Relio.Application.Reminders;

/// <summary>
/// Input for updating an existing reminder.
/// </summary>
public sealed record UpdateReminderRequest
{
    /// <summary>What to remember.</summary>
    public required string Title { get; init; }

    /// <summary>The scheduled due date.</summary>
    public required DateOnly DueDate { get; init; }

    /// <summary>The frequency of repetition, or <see cref="ReminderFrequency.Once"/>.</summary>
    public ReminderFrequency Frequency { get; init; } = ReminderFrequency.Once;

    /// <summary>Interval in months when <see cref="Frequency"/> is <see cref="ReminderFrequency.CustomMonths"/>.</summary>
    public int? CustomIntervalMonths { get; init; }
}
