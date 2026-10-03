using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Time;

namespace Relio.Application.Tests.Time;

/// <summary>
/// Proves issue #12's acceptance criteria against <see cref="UserCalendar"/> directly: a
/// reminder due "today" is today in the user's time zone, including for users far from UTC, and
/// across a DST transition.
/// </summary>
public class UserCalendarTests
{
    [Fact]
    public void ToUserDate_in_Pacific_Kiritimati_is_already_the_next_day_relative_to_UTC_near_midnight()
    {
        // UTC+14 - the world's furthest-ahead time zone.
        var timeZone = TimeZoneIds.Parse("Pacific/Kiritimati");
        var instant = DateTimeOffset.Parse("2024-03-01T20:00:00Z", CultureInfo.InvariantCulture);

        UserCalendar.ToUserDate(instant, timeZone).Should().Be(new DateOnly(2024, 3, 2));
    }

    [Fact]
    public void ToUserDate_in_Pacific_Pago_Pago_is_still_the_previous_day_relative_to_UTC_near_midnight()
    {
        // UTC-11 - one of the world's furthest-behind time zones.
        var timeZone = TimeZoneIds.Parse("Pacific/Pago_Pago");
        var instant = DateTimeOffset.Parse("2024-03-02T03:00:00Z", CultureInfo.InvariantCulture);

        UserCalendar.ToUserDate(instant, timeZone).Should().Be(new DateOnly(2024, 3, 1));
    }

    [Fact]
    public void IsDueToday_is_true_in_Kiritimati_while_still_false_in_UTC_for_the_same_instant()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2024-03-01T20:00:00Z", CultureInfo.InvariantCulture));
        var reminderDate = new DateOnly(2024, 3, 2);

        var todayInKiritimati = UserCalendar.Today(timeProvider, TimeZoneIds.Parse("Pacific/Kiritimati"));
        var todayInUtc = UserCalendar.Today(timeProvider, TimeZoneInfo.Utc);

        UserCalendar.IsDueToday(reminderDate, todayInKiritimati).Should().BeTrue();
        UserCalendar.IsDueToday(reminderDate, todayInUtc).Should().BeFalse();
    }

    [Fact]
    public void IsOverdue_is_false_in_Pago_Pago_while_already_true_in_UTC_for_the_same_instant()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2024-03-02T03:00:00Z", CultureInfo.InvariantCulture));
        var reminderDate = new DateOnly(2024, 3, 1);

        var todayInPagoPago = UserCalendar.Today(timeProvider, TimeZoneIds.Parse("Pacific/Pago_Pago"));
        var todayInUtc = UserCalendar.Today(timeProvider, TimeZoneInfo.Utc);

        UserCalendar.IsOverdue(reminderDate, todayInPagoPago).Should().BeFalse();
        UserCalendar.IsOverdue(reminderDate, todayInUtc).Should().BeTrue();
    }

    [Theory]
    [InlineData("2024-03-10T06:59:00Z")] // 01:59 EST, just before the spring-forward transition.
    [InlineData("2024-03-10T07:01:00Z")] // 03:01 EDT - 02:00-03:00 local does not exist that day.
    public void ToUserDate_resolves_the_correct_calendar_date_across_a_DST_spring_forward(string utcInstant)
    {
        var timeZone = TimeZoneIds.Parse("America/New_York");
        var instant = DateTimeOffset.Parse(utcInstant, CultureInfo.InvariantCulture);

        UserCalendar.ToUserDate(instant, timeZone).Should().Be(new DateOnly(2024, 3, 10));
    }

    [Theory]
    [InlineData("2024-10-27T00:59:00Z")] // 02:59 CEST, just before Europe/Rome falls back.
    [InlineData("2024-10-27T01:01:00Z")] // 02:01 CET - the repeated hour, now on standard time.
    public void ToUserDate_resolves_the_correct_calendar_date_across_a_DST_fall_back(string utcInstant)
    {
        var timeZone = TimeZoneIds.Parse("Europe/Rome");
        var instant = DateTimeOffset.Parse(utcInstant, CultureInfo.InvariantCulture);

        UserCalendar.ToUserDate(instant, timeZone).Should().Be(new DateOnly(2024, 10, 27));
    }

    [Fact]
    public void NextOccurrence_of_a_Feb29_birthday_in_a_non_leap_year_is_Feb28()
    {
        var birthday = new DateOnly(2000, 2, 29);
        var today = new DateOnly(2025, 1, 1); // 2025 is not a leap year.

        UserCalendar.NextOccurrence(birthday, today).Should().Be(new DateOnly(2025, 2, 28));
    }

    [Fact]
    public void NextOccurrence_of_a_Feb29_birthday_in_a_leap_year_is_Feb29()
    {
        var birthday = new DateOnly(2000, 2, 29);
        var today = new DateOnly(2024, 1, 1); // 2024 is a leap year.

        UserCalendar.NextOccurrence(birthday, today).Should().Be(new DateOnly(2024, 2, 29));
    }

    [Fact]
    public void NextOccurrence_wraps_to_next_year_once_this_years_date_has_passed()
    {
        var birthday = new DateOnly(1990, 5, 10);
        var today = new DateOnly(2024, 6, 1);

        UserCalendar.NextOccurrence(birthday, today).Should().Be(new DateOnly(2025, 5, 10));
    }

    [Fact]
    public void NextOccurrence_returns_today_when_the_birthday_is_today()
    {
        var birthday = new DateOnly(1990, 6, 1);
        var today = new DateOnly(2024, 6, 1);

        UserCalendar.NextOccurrence(birthday, today).Should().Be(today);
    }
}
