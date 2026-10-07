using Relio.Application.Time;
using Relio.Domain;

namespace Relio.Application.Reminders;

/// <summary>
/// Pure calendar calculations for stay-in-touch reach out cadence (issue #41, epic #36).
/// No database, no current user, no clock.
/// </summary>
public static class ReachOutCalculator
{
    /// <summary>
    /// Calculates an overdue reach-out suggestion for <paramref name="person"/> given today's date and the user's time zone.
    /// Returns null if person is archived, has no cadence set, or is not overdue.
    /// </summary>
    public static ReachOutDto? Calculate(
        Person person,
        DateOnly today,
        TimeZoneInfo userTimeZone)
    {
        ArgumentNullException.ThrowIfNull(person);

        if (person.IsArchived)
        {
            return null;
        }

        return Calculate(
            person.Id,
            person.DisplayName,
            person.LastContactedOn,
            person.CreatedAtUtc,
            person.StayInTouchCadenceDays,
            today,
            userTimeZone);
    }

    /// <summary>
    /// Calculates an overdue reach-out suggestion given explicit date and cadence parameters.
    /// Returns null if cadence is null or not positive, or if not overdue.
    /// </summary>
    public static ReachOutDto? Calculate(
        DateOnly? lastContactedOn,
        DateTime createdAtUtc,
        int? cadenceDays,
        DateOnly today,
        TimeZoneInfo userTimeZone) =>
        Calculate(Guid.Empty, string.Empty, lastContactedOn, createdAtUtc, cadenceDays, today, userTimeZone);

    /// <summary>
    /// Calculates an overdue reach-out suggestion given explicit parameters including person identity.
    /// Returns null if cadence is null or not positive, or if not overdue.
    /// </summary>
    public static ReachOutDto? Calculate(
        Guid personId,
        string personDisplayName,
        DateOnly? lastContactedOn,
        DateTime createdAtUtc,
        int? cadenceDays,
        DateOnly today,
        TimeZoneInfo userTimeZone)
    {
        ArgumentNullException.ThrowIfNull(userTimeZone);

        if (cadenceDays is null or <= 0)
        {
            return null;
        }

        var referenceDate = lastContactedOn ?? UserCalendar.ToUserDate(
            new DateTimeOffset(DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc)),
            userTimeZone);

        var daysSinceContact = today.DayNumber - referenceDate.DayNumber;

        if (daysSinceContact <= cadenceDays.Value)
        {
            return null;
        }

        var daysOverdue = daysSinceContact - cadenceDays.Value;

        return new ReachOutDto(
            personId,
            personDisplayName,
            cadenceDays.Value,
            referenceDate,
            daysSinceContact,
            daysOverdue);
    }
}
