using Relio.Application.Time;
using Relio.Domain;

namespace Relio.Application.Reminders;

/// <summary>
/// Pure calendar calculations for birthday reminders (issue #38, epic #36).
/// No database, no current user, no clock.
/// </summary>
public static class BirthdayReminderCalculator
{
    /// <summary>
    /// Calculates a birthday reminder for <paramref name="person"/> given the user's default lead days and today's date.
    /// Returns null if globally disabled, person reminder disabled, person is archived, or person has no birthday.
    /// </summary>
    public static BirthdayReminderDto? Calculate(
        Person person,
        int defaultLeadDays,
        DateOnly today,
        bool globallyEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(person);

        if (person.IsArchived)
        {
            return null;
        }

        return Calculate(
            person.Id,
            person.DisplayName,
            person.Birthday,
            person.BirthdayReminderDisabled,
            person.BirthdayReminderLeadDays,
            defaultLeadDays,
            today,
            globallyEnabled);
    }

    /// <summary>
    /// Calculates a birthday reminder given explicit parameters.
    /// Returns null if globally disabled, reminder disabled, or birthday is null.
    /// </summary>
    public static BirthdayReminderDto? Calculate(
        Guid personId,
        string personDisplayName,
        Birthday? birthday,
        bool reminderDisabled,
        int? personLeadDays,
        int defaultLeadDays,
        DateOnly today,
        bool globallyEnabled = true)
    {
        if (!globallyEnabled || reminderDisabled || birthday is null)
        {
            return null;
        }

        var leadDays = Math.Max(0, personLeadDays ?? defaultLeadDays);
        var nextOccurrence = UserCalendar.NextOccurrence(birthday, today);
        var reminderDate = nextOccurrence.AddDays(-leadDays);
        var isDue = today >= reminderDate && today <= nextOccurrence;
        var turningAge = birthday.Year.HasValue ? nextOccurrence.Year - birthday.Year.Value : (int?)null;
        var daysUntilBirthday = nextOccurrence.DayNumber - today.DayNumber;

        return new BirthdayReminderDto(
            personId,
            personDisplayName,
            nextOccurrence,
            turningAge,
            reminderDate,
            leadDays,
            isDue,
            daysUntilBirthday);
    }
}
