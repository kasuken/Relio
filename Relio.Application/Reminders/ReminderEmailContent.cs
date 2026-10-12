namespace Relio.Application.Reminders;

/// <summary>
/// The fixed words a reminder email uses where the data is not the user's own text. Emails carry
/// only a person's name, a title and a date, never notes or private narratives (issue #121).
/// </summary>
public static class ReminderEmailContent
{
    /// <summary>The title of a birthday notification.</summary>
    public const string BirthdayTitle = "Birthday";

    /// <summary>The name used when a reminder's person cannot be resolved.</summary>
    public const string FallbackPersonName = "Contact";
}
