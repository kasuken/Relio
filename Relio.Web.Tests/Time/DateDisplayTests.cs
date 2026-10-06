using Relio.Web.Time;

namespace Relio.Web.Tests.Time;

public class DateDisplayTests
{
    [Fact]
    public void Omits_the_year_for_the_current_year()
    {
        DateDisplay.Format(new DateOnly(2026, 3, 3), new DateOnly(2026, 10, 6)).Should().Be("3 March");
    }

    [Theory]
    [InlineData("2026-10-06", "Today")]
    [InlineData("2026-10-05", "Yesterday")]
    [InlineData("2026-10-04", "2 days ago")]
    [InlineData("2026-09-24", "12 days ago")]
    [InlineData("2026-09-23", "13 days ago")]
    public void FormatRelative_uses_today_yesterday_and_days_ago_for_two_weeks(string date, string expected)
    {
        DateDisplay.FormatRelative(DateOnly.Parse(date), new DateOnly(2026, 10, 6)).Should().Be(expected);
    }

    [Theory]
    [InlineData("2026-09-22", "22 September")]
    [InlineData("2026-03-03", "3 March")]
    [InlineData("2025-03-03", "3 March 2025")]
    [InlineData("2026-10-07", "7 October")]
    [InlineData("2027-01-01", "1 January 2027")]
    public void FormatRelative_falls_back_to_the_date_after_two_weeks_and_for_future_dates(string date, string expected)
    {
        DateDisplay.FormatRelative(DateOnly.Parse(date), new DateOnly(2026, 10, 6)).Should().Be(expected);
    }

    [Fact]
    public void Includes_the_year_for_any_other_year()
    {
        DateDisplay.Format(new DateOnly(2025, 3, 3), new DateOnly(2026, 10, 6)).Should().Be("3 March 2025");
        DateDisplay.Format(new DateOnly(2027, 1, 1), new DateOnly(2026, 12, 31)).Should().Be("1 January 2027");
    }
}
