using System.Globalization;
using Relio.Domain;

namespace Relio.Application.People.Import;

/// <summary>The order the parts of a written date come in (the same file never mixes them).</summary>
public enum DateOrder
{
    /// <summary>1985-04-15.</summary>
    YearMonthDay,

    /// <summary>15/04/1985.</summary>
    DayMonthYear,

    /// <summary>04/15/1985.</summary>
    MonthDayYear,
}

/// <summary>
/// Reads the birthdays of an imported file, strictly: a form it does not know is "unreadable" (the
/// field is left out and the row says so) rather than guessed. Pure and culture independent: only
/// <see cref="int.TryParse(string, NumberStyles, IFormatProvider, out int)"/> with the invariant
/// culture, never <c>DateTime.Parse</c>, never the current culture, never a clock.
/// </summary>
public static class BirthdayParser
{
    /// <summary>The year Apple writes for a birthday without a year.</summary>
    private const int AppleOmittedYear = 1604;

    /// <summary>
    /// Reads a vCard <c>BDAY</c> value: <c>yyyy-MM-dd</c>, <c>yyyyMMdd</c>, <c>--MM-dd</c> or <c>--MMdd</c>,
    /// optionally followed by <c>T</c> and a time (dropped). The year 1604, or the year named by
    /// <paramref name="appleOmitYear"/> (the <c>X-APPLE-OMIT-YEAR</c> parameter), means "no year".
    /// </summary>
    public static bool TryParseVCard(string? value, string? appleOmitYear, out ImportBirthdayDraft? birthday)
    {
        birthday = null;
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var time = text.IndexOf('T', StringComparison.OrdinalIgnoreCase);
        if (time >= 0)
        {
            text = text[..time];
        }

        if (!TryParseIso(text, out var month, out var day, out var year))
        {
            return false;
        }

        if (year is int y && (y == AppleOmittedYear || (int.TryParse(appleOmitYear, NumberStyles.None, CultureInfo.InvariantCulture, out var omit) && y == omit)))
        {
            year = null;
        }

        return Create(month, day, year, out birthday);
    }

    /// <summary>
    /// Reads a CSV birthday cell. Blank, <c>0/0/00</c> and <c>00/00/0000</c> (Outlook's "none") mean no
    /// birthday and succeed with <paramref name="birthday"/> <see langword="null"/>. Otherwise the ISO forms of
    /// <see cref="TryParseVCard"/>, <c>yyyy/MM/dd</c>, and <c>a/b/yyyy</c>, <c>a.b.yyyy</c> or <c>a-b-yyyy</c> read in
    /// <paramref name="order"/>. Two-digit years, month names and times are not read.
    /// </summary>
    public static bool TryParseCsv(string? value, DateOrder order, out ImportBirthdayDraft? birthday)
    {
        birthday = null;
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || IsOutlookNone(text))
        {
            return true;
        }

        if (TryParseIso(text, out var month, out var day, out var year))
        {
            return Create(month, day, year, out birthday);
        }

        if (!TrySplitDate(text, out var first, out var second, out var third, out var yearFirst))
        {
            return false;
        }

        if (yearFirst)
        {
            // yyyy/MM/dd (also yyyy.MM.dd): the year leads, so the order setting does not matter.
            return Create(second, third, first, out birthday);
        }

        return order switch
        {
            DateOrder.DayMonthYear => Create(second, first, third, out birthday),
            DateOrder.MonthDayYear => Create(first, second, third, out birthday),
            _ => false,
        };
    }

    /// <summary>
    /// Splits <c>a/b/yyyy</c>-style text (separator <c>/</c>, <c>.</c> or <c>-</c>, the same twice) into its numbers.
    /// <paramref name="yearFirst"/> is set for <c>yyyy/MM/dd</c>; otherwise the third number is the year.
    /// </summary>
    internal static bool TrySplitDate(string text, out int first, out int second, out int third, out bool yearFirst)
    {
        first = second = third = 0;
        yearFirst = false;

        var separator = text.Contains('/', StringComparison.Ordinal) ? '/' : text.Contains('.', StringComparison.Ordinal) ? '.' : '-';
        var parts = text.Split(separator);
        if (parts.Length != 3 || parts.Any(part => part.Length is 0 or > 4 || !IsDigits(part)))
        {
            return false;
        }

        if (parts[0].Length == 4 && parts[1].Length <= 2 && parts[2].Length <= 2)
        {
            yearFirst = true;
        }
        else if (!(parts[0].Length <= 2 && parts[1].Length <= 2 && parts[2].Length == 4))
        {
            return false;
        }

        return int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out first)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out second)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out third);
    }

    private static bool IsOutlookNone(string text)
    {
        // 0/0/00, 00/00/0000: only zeros and separators.
        var sawZero = false;
        foreach (var character in text)
        {
            if (character == '0')
            {
                sawZero = true;
            }
            else if (character is not ('/' or '.' or '-'))
            {
                return false;
            }
        }

        return sawZero;
    }

    private static bool TryParseIso(string text, out int month, out int day, out int? year)
    {
        month = day = 0;
        year = null;

        // --MM-dd, --MMdd
        if (text.StartsWith("--", StringComparison.Ordinal))
        {
            var rest = text[2..];
            return rest.Length switch
            {
                5 when rest[2] == '-' => TryInts(rest[..2], rest[3..], out month, out day),
                4 => TryInts(rest[..2], rest[2..], out month, out day),
                _ => false,
            };
        }

        // yyyy-MM-dd
        if (text.Length == 10 && text[4] == '-' && text[7] == '-')
        {
            return TryYearMonthDay(text[..4], text[5..7], text[8..], out month, out day, out year);
        }

        // yyyyMMdd
        if (text.Length == 8 && IsDigits(text))
        {
            return TryYearMonthDay(text[..4], text[4..6], text[6..], out month, out day, out year);
        }

        return false;
    }

    private static bool TryYearMonthDay(string yearText, string monthText, string dayText, out int month, out int day, out int? year)
    {
        year = null;
        month = day = 0;
        if (!IsDigits(yearText) || !int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedYear))
        {
            return false;
        }

        year = parsedYear;
        return TryInts(monthText, dayText, out month, out day);
    }

    private static bool TryInts(string monthText, string dayText, out int month, out int day)
    {
        month = day = 0;
        return IsDigits(monthText)
            && IsDigits(dayText)
            && int.TryParse(monthText, NumberStyles.None, CultureInfo.InvariantCulture, out month)
            && int.TryParse(dayText, NumberStyles.None, CultureInfo.InvariantCulture, out day);
    }

    private static bool Create(int month, int day, int? year, out ImportBirthdayDraft? birthday)
    {
        if (Birthday.IsValid(month, day, year))
        {
            birthday = new ImportBirthdayDraft(month, day, year);
            return true;
        }

        birthday = null;
        return false;
    }

    private static bool IsDigits(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        foreach (var character in text)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
