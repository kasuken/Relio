namespace Relio.Application.Reminders;

/// <summary>
/// Machine-readable codes for validation failures on reminder input.
/// </summary>
public enum ReminderValidationError
{
    /// <summary>The title is missing or whitespace only.</summary>
    TitleRequired,

    /// <summary>The title exceeds the maximum allowed length.</summary>
    TitleTooLong,

    /// <summary>The recurrence frequency is not defined.</summary>
    InvalidFrequency,

    /// <summary>Custom interval months must be greater than zero when CustomMonths frequency is chosen.</summary>
    InvalidCustomInterval,
}
