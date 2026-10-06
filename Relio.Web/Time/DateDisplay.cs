using System.Globalization;

namespace Relio.Web.Time;

/// <summary>
/// Formats calendar dates the way the design system writes them: "3 March", with the year only
/// when it is not the current one ("3 March 2025"). Pure, so it is unit tested without a component.
/// </summary>
public static class DateDisplay
{
    /// <summary>Formats <paramref name="date"/> relative to <paramref name="today"/>.</summary>
    public static string Format(DateOnly date, DateOnly today) =>
        date.ToString(date.Year == today.Year ? "d MMMM" : "d MMMM yyyy", CultureInfo.InvariantCulture);
}
