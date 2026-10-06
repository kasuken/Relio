using System.Globalization;

namespace Relio.Web.Time;

/// <summary>
/// Formats calendar dates the way the design system writes them: "Today", "Yesterday", "12 days
/// ago", then "3 March", with the year only when it is not the current one ("3 March 2025").
/// Pure, so it is unit tested without a component.
/// </summary>
public static class DateDisplay
{
    /// <summary>The longest gap, in days, that still reads as "n days ago" rather than as a date.</summary>
    private const int LastDaysAgoGap = 13;

    /// <summary>Formats <paramref name="date"/> relative to <paramref name="today"/>.</summary>
    public static string Format(DateOnly date, DateOnly today) =>
        date.ToString(date.Year == today.Year ? "d MMMM" : "d MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a past <paramref name="date"/> the human way: "Today", "Yesterday", "2 days ago" up
    /// to "13 days ago", and from two weeks on - or for a date in the future, which a "days ago"
    /// phrase would get wrong - the plain date from <see cref="Format"/>.
    /// </summary>
    public static string FormatRelative(DateOnly date, DateOnly today)
    {
        var daysAgo = today.DayNumber - date.DayNumber;
        return daysAgo switch
        {
            0 => "Today",
            1 => "Yesterday",
            >= 2 and <= LastDaysAgoGap => $"{daysAgo.ToString(CultureInfo.InvariantCulture)} days ago",
            _ => Format(date, today),
        };
    }
}
