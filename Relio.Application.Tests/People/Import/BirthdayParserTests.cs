using System.Globalization;
using Relio.Application.People.Import;

namespace Relio.Application.Tests.People.Import;

public sealed class BirthdayParserTests
{
    [Theory]
    [InlineData("1985-04-15", null, 4, 15, 1985)]
    [InlineData("19850415", null, 4, 15, 1985)]
    [InlineData("--04-15", null, 4, 15, null)]
    [InlineData("--0415", null, 4, 15, null)]
    [InlineData("1985-04-15T10:00:00", null, 4, 15, 1985)]
    [InlineData("1604-04-15", null, 4, 15, null)]
    [InlineData("1900-04-15", "1900", 4, 15, null)]
    [InlineData("1900-04-15", "1604", 4, 15, 1900)]
    [InlineData("  1985-04-15  ", null, 4, 15, 1985)]
    public void Reads_the_vcard_forms(string value, string? omit, int month, int day, int? year)
    {
        BirthdayParser.TryParseVCard(value, omit, out var birthday).Should().BeTrue();
        birthday.Should().Be(new ImportBirthdayDraft(month, day, year));
    }

    [Theory]
    [InlineData("")]
    [InlineData("circa 1800")]
    [InlineData("15/04/1985")]
    [InlineData("1985-4-15")]
    [InlineData("1985-02-30")]
    [InlineData("--0230")]
    [InlineData("--13-01")]
    [InlineData("0000-04-15")]
    [InlineData("abcd-04-15")]
    public void Rejects_other_vcard_forms(string value)
    {
        BirthdayParser.TryParseVCard(value, null, out var birthday).Should().BeFalse();
        birthday.Should().BeNull();
    }

    [Theory]
    [InlineData("15/04/1985", DateOrder.DayMonthYear, 4, 15, 1985)]
    [InlineData("15.04.1985", DateOrder.DayMonthYear, 4, 15, 1985)]
    [InlineData("15-04-1985", DateOrder.DayMonthYear, 4, 15, 1985)]
    [InlineData("4/15/1985", DateOrder.MonthDayYear, 4, 15, 1985)]
    [InlineData("04.15.1985", DateOrder.MonthDayYear, 4, 15, 1985)]
    [InlineData("1985/04/15", DateOrder.DayMonthYear, 4, 15, 1985)]
    [InlineData("1985.04.15", DateOrder.MonthDayYear, 4, 15, 1985)]
    [InlineData("1985-04-15", DateOrder.YearMonthDay, 4, 15, 1985)]
    [InlineData("--04-15", DateOrder.MonthDayYear, 4, 15, null)]
    [InlineData("03/04/1990", DateOrder.DayMonthYear, 4, 3, 1990)]
    [InlineData("03/04/1990", DateOrder.MonthDayYear, 3, 4, 1990)]
    public void Reads_the_csv_forms_in_the_given_order(string value, DateOrder order, int month, int day, int? year)
    {
        BirthdayParser.TryParseCsv(value, order, out var birthday).Should().BeTrue();
        birthday.Should().Be(new ImportBirthdayDraft(month, day, year));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0/0/00")]
    [InlineData("00/00/0000")]
    [InlineData("0000-00-00")]
    public void Outlook_zero_dates_mean_no_birthday(string value)
    {
        BirthdayParser.TryParseCsv(value, DateOrder.MonthDayYear, out var birthday).Should().BeTrue();
        birthday.Should().BeNull();
    }

    [Theory]
    [InlineData("15/04/85")]
    [InlineData("15 April 1985")]
    [InlineData("April 15, 1985")]
    [InlineData("15/04/1985 10:00")]
    [InlineData("2/30/1990")]
    [InlineData("31/04/1985")]
    [InlineData("13/13/1985")]
    [InlineData("1/2")]
    [InlineData("a/b/cccc")]
    public void Rejects_two_digit_years_month_names_and_impossible_dates(string value)
    {
        BirthdayParser.TryParseCsv(value, DateOrder.DayMonthYear, out var birthday).Should().BeFalse();
        birthday.Should().BeNull();
    }

    [Fact]
    public void A_year_first_order_does_not_guess_day_first_dates()
    {
        BirthdayParser.TryParseCsv("15/04/1985", DateOrder.YearMonthDay, out _).Should().BeFalse();
    }

    [Fact]
    public void Accepts_yearless_29_february()
    {
        BirthdayParser.TryParseVCard("--0229", null, out var birthday).Should().BeTrue();
        birthday.Should().Be(new ImportBirthdayDraft(2, 29, null));

        BirthdayParser.TryParseVCard("1985-02-29", null, out _).Should().BeFalse();
        BirthdayParser.TryParseVCard("1984-02-29", null, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("ar-SA")]
    public void Ignores_the_current_culture(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            BirthdayParser.TryParseCsv("15/04/1985", DateOrder.DayMonthYear, out var csv).Should().BeTrue();
            csv.Should().Be(new ImportBirthdayDraft(4, 15, 1985));
            BirthdayParser.TryParseVCard("19850415", null, out var vcard).Should().BeTrue();
            vcard.Should().Be(new ImportBirthdayDraft(4, 15, 1985));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
