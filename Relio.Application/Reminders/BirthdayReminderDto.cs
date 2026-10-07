namespace Relio.Application.Reminders;

/// <summary>
/// A birthday reminder for display, scoped to the current user (epic #36, issue #38).
/// </summary>
public sealed record BirthdayReminderDto(
    Guid PersonId,
    string PersonDisplayName,
    DateOnly BirthdayDate,
    int? TurningAge,
    DateOnly ReminderDate,
    int LeadDays,
    bool IsDue,
    int DaysUntilBirthday);
