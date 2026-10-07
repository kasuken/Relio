using Relio.Domain;

namespace Relio.Application.Reminders;

/// <summary>
/// Input for creating a new reconnect reminder.
/// </summary>
public sealed record CreateReminderRequest
{
    /// <summary>The person this reminder belongs to. Must belong to the current user.</summary>
    public required Guid PersonId { get; init; }

    /// <summary>What to remember (e.g. "Catch up over coffee").</summary>
    public required string Title { get; init; }

    /// <summary>The scheduled due date.</summary>
    public required DateOnly DueDate { get; init; }

    /// <summary>The frequency of repetition, or <see cref="ReminderFrequency.Once"/>.</summary>
    public ReminderFrequency Frequency { get; init; } = ReminderFrequency.Once;

    /// <summary>Interval in months when <see cref="Frequency"/> is <see cref="ReminderFrequency.CustomMonths"/>.</summary>
    public int? CustomIntervalMonths { get; init; }
}
