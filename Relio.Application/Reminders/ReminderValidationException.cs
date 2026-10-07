namespace Relio.Application.Reminders;

/// <summary>
/// Thrown when reminder input breaks a validation rule in <see cref="ReminderRules"/>.
/// Carries machine-readable error codes; never user input.
/// </summary>
public sealed class ReminderValidationException(IReadOnlyList<ReminderValidationError> errors)
    : Exception($"Reminder validation failed: {string.Join(", ", errors)}")
{
    /// <summary>The validation errors that caused this exception.</summary>
    public IReadOnlyList<ReminderValidationError> Errors { get; } = errors;
}
