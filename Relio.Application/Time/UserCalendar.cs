using Relio.Domain;

namespace Relio.Application.Time;

/// <summary>
/// Pure, synchronous calendar-date helpers for a given <see cref="TimeZoneInfo"/> - no current
/// user, no database, no I/O. <see cref="IUserTimeZoneService"/> builds on these for the current
/// user; tests exercise them directly (with a fake <see cref="TimeProvider"/>) for fast,
/// deterministic coverage of time-zone-far-from-UTC and DST scenarios. See the "Dates and time
/// zones" section of AGENTS.md: calendar dates are always <see cref="DateOnly"/>, audit timestamps
/// are always UTC, and the only place they meet is here.
/// </summary>
public static class UserCalendar
{
    /// <summary>Converts a UTC instant to the calendar date it falls on in <paramref name="timeZone"/>.</summary>
    public static DateOnly ToUserDate(DateTimeOffset utcInstant, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var local = TimeZoneInfo.ConvertTime(utcInstant, timeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    /// <summary>
    /// "Today", in <paramref name="timeZone"/>, as read from <paramref name="timeProvider"/>. This
    /// is the one place "now" should be read for calendar-date logic - never
    /// <see cref="DateTime.UtcNow"/> or <see cref="DateTime.Now"/> (see AGENTS.md).
    /// </summary>
    public static DateOnly Today(TimeProvider timeProvider, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return ToUserDate(timeProvider.GetUtcNow(), timeZone);
    }

    /// <summary>Whether <paramref name="date"/> is due today - i.e. equal to <paramref name="today"/>.</summary>
    public static bool IsDueToday(DateOnly date, DateOnly today) => date == today;

    /// <summary>Whether <paramref name="date"/> is overdue - i.e. strictly before <paramref name="today"/>.</summary>
    public static bool IsOverdue(DateOnly date, DateOnly today) => date < today;

    /// <summary>
    /// The next occurrence of <paramref name="birthday"/> on or after <paramref name="today"/>
    /// (i.e. today counts as "next" if the birthday is today). A birthday of February 29th that
    /// does not fall in a leap year is observed on <b>February 28th</b> of that year (the
    /// alternative, March 1st, was considered and rejected - see the "Dates and time zones"
    /// section of AGENTS.md).
    /// </summary>
    public static DateOnly NextOccurrence(DateOnly birthday, DateOnly today) =>
        NextOccurrence(Birthday.Create(birthday.Month, birthday.Day, birthday.Year), today);

    /// <summary>
    /// The next occurrence of <paramref name="birthday"/> on or after <paramref name="today"/>,
    /// whether or not its year is known - the year of birth plays no part. The February 28th rule
    /// of <see cref="NextOccurrence(DateOnly, DateOnly)"/> applies to a year-less 29 February too.
    /// </summary>
    public static DateOnly NextOccurrence(Birthday birthday, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(birthday);

        var candidate = OccurrenceInYear(birthday, today.Year);
        return candidate >= today ? candidate : OccurrenceInYear(birthday, today.Year + 1);
    }

    private static DateOnly OccurrenceInYear(Birthday birthday, int year)
    {
        if (birthday.Month == 2 && birthday.Day == 29 && !DateTime.IsLeapYear(year))
        {
            return new DateOnly(year, 2, 28);
        }

        return new DateOnly(year, birthday.Month, birthday.Day);
    }
}
