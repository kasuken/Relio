using System.Diagnostics.CodeAnalysis;

namespace Relio.Domain;

/// <summary>
/// A person's birthday: a day and a month, and optionally the year they were born. It is a value
/// type of its own - not a <see cref="DateOnly"/> - because the year is often unknown ("14 March"
/// is a perfectly good birthday), and a <see cref="DateOnly"/> cannot say "no year" without
/// inventing one that then leaks into ages and exports. Like every calendar date in Relio it has
/// no time-of-day or time zone and is never converted to or from UTC (see the "Dates and time
/// zones" section of AGENTS.md).
/// </summary>
/// <remarks>
/// <see cref="Person"/> stores it as three nullable columns (<c>BirthdayYear</c>,
/// <c>BirthdayMonth</c>, <c>BirthdayDay</c>) so the database can filter on month and day without
/// parsing anything; this type is the validated view of those columns.
/// </remarks>
public sealed record Birthday
{
    /// <summary>The smallest birth year Relio accepts (the smallest year a <see cref="DateOnly"/> holds).</summary>
    public const int MinYear = 1;

    /// <summary>The largest birth year Relio accepts (the largest year a <see cref="DateOnly"/> holds).</summary>
    public const int MaxYear = 9999;

    /// <summary>
    /// The leap year used to check a year-less day and month: with it, 29 February is a valid
    /// birthday without a year, as it is with a leap year.
    /// </summary>
    private const int ReferenceLeapYear = 2000;

    private Birthday(int month, int day, int? year)
    {
        Month = month;
        Day = day;
        Year = year;
    }

    /// <summary>The month, 1 to 12.</summary>
    public int Month { get; }

    /// <summary>The day of the month, 1 to 31 (never past the end of <see cref="Month"/>).</summary>
    public int Day { get; }

    /// <summary>The year of birth, or <see langword="null"/> when it is not known.</summary>
    public int? Year { get; }

    /// <summary>
    /// The full date of birth, or <see langword="null"/> when <see cref="Year"/> is not known.
    /// </summary>
    public DateOnly? Date => Year is int year ? new DateOnly(year, Month, Day) : null;

    /// <summary>
    /// Whether <paramref name="month"/>, <paramref name="day"/> and the optional
    /// <paramref name="year"/> describe a date that exists. Without a year, 29 February is valid.
    /// </summary>
    public static bool IsValid(int month, int day, int? year)
    {
        if (month is < 1 or > 12)
        {
            return false;
        }

        if (year is int y && (y < MinYear || y > MaxYear))
        {
            return false;
        }

        return day >= 1 && day <= DateTime.DaysInMonth(year ?? ReferenceLeapYear, month);
    }

    /// <summary>Creates a birthday when the parts describe a date that exists; otherwise returns <see langword="false"/>.</summary>
    public static bool TryCreate(int month, int day, int? year, [NotNullWhen(true)] out Birthday? birthday)
    {
        if (IsValid(month, day, year))
        {
            birthday = new Birthday(month, day, year);
            return true;
        }

        birthday = null;
        return false;
    }

    /// <summary>Creates a birthday, throwing when the parts do not describe a date that exists.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The month, day or year is out of range.</exception>
    public static Birthday Create(int month, int day, int? year = null) =>
        TryCreate(month, day, year, out var birthday)
            ? birthday
            : throw new ArgumentOutOfRangeException(
                nameof(day), "The month, day and year do not describe a date that exists.");
}
