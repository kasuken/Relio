using Relio.Domain;

namespace Relio.Application.Reminders;

/// <summary>
/// Pure validation and normalization rules for reminders.
/// </summary>
public static class ReminderRules
{
    /// <summary>Trims title text, returning empty string if null.</summary>
    public static string NormalizeTitle(string? title) => title?.Trim() ?? string.Empty;

    /// <summary>Validates creation inputs and returns any errors.</summary>
    public static IReadOnlyList<ReminderValidationError> Validate(CreateReminderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Validate(request.Title, request.Frequency, request.CustomIntervalMonths);
    }

    /// <summary>Validates update inputs and returns any errors.</summary>
    public static IReadOnlyList<ReminderValidationError> Validate(UpdateReminderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Validate(request.Title, request.Frequency, request.CustomIntervalMonths);
    }

    private static List<ReminderValidationError> Validate(string title, ReminderFrequency frequency, int? customIntervalMonths)
    {
        var errors = new List<ReminderValidationError>();
        var normalizedTitle = NormalizeTitle(title);

        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            errors.Add(ReminderValidationError.TitleRequired);
        }
        else if (normalizedTitle.Length > Reminder.TitleMaxLength)
        {
            errors.Add(ReminderValidationError.TitleTooLong);
        }

        if (!Enum.IsDefined(frequency))
        {
            errors.Add(ReminderValidationError.InvalidFrequency);
        }
        else if (frequency == ReminderFrequency.CustomMonths && (customIntervalMonths is null || customIntervalMonths <= 0))
        {
            errors.Add(ReminderValidationError.InvalidCustomInterval);
        }

        return errors;
    }
}
