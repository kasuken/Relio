namespace Relio.Application.Reminders;

/// <summary>
/// An overdue reach-out suggestion based on a person's stay-in-touch cadence (epic #36, issue #41).
/// </summary>
public sealed record ReachOutDto(
    Guid PersonId,
    string PersonDisplayName,
    int CadenceDays,
    DateOnly ReferenceDate,
    int DaysSinceContact,
    int DaysOverdue);
