using Relio.Domain;

namespace Relio.Application.Reminders;

/// <summary>
/// A reminder for display, scoped to the current user.
/// </summary>
public sealed record ReminderDto(
    Guid Id,
    Guid PersonId,
    string PersonDisplayName,
    string Title,
    DateOnly DueDate,
    ReminderFrequency Frequency,
    int? CustomIntervalMonths,
    DateOnly? SnoozedUntilDate,
    DateOnly EffectiveDueDate,
    bool IsCompleted,
    DateTime? CompletedAtUtc,
    DateOnly? LastDeliveredDate);
