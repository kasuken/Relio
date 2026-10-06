using System.Globalization;
using Relio.Domain;

namespace Relio.Web.Time;

/// <summary>
/// Formats a <see cref="Birthday"/> the way the design system writes dates: "14 March" when the
/// year is not known, "10 December 1815" when it is. Pure, so it is unit tested without a component.
/// </summary>
public static class BirthdayDisplay
{
    /// <summary>Formats <paramref name="birthday"/> as day, month name and - only when known - year.</summary>
    public static string Format(Birthday birthday)
    {
        ArgumentNullException.ThrowIfNull(birthday);

        var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(birthday.Month);
        return birthday.Year is int year
            ? $"{birthday.Day} {monthName} {year}"
            : $"{birthday.Day} {monthName}";
    }
}
