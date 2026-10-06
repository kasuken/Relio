using Relio.Domain;

namespace Relio.Application.Tests.People;

/// <summary>The <see cref="Birthday"/> value type: which day, month and year combinations are real.</summary>
public class BirthdayTests
{
    [Theory]
    [InlineData(1, 1, null)]
    [InlineData(12, 31, null)]
    [InlineData(2, 29, null)] // a year-less 29 February is valid: some years have it.
    [InlineData(2, 29, 2024)]
    [InlineData(2, 28, 2023)]
    [InlineData(3, 14, 1990)]
    [InlineData(6, 15, 1)]
    [InlineData(6, 15, 9999)]
    public void IsValid_accepts_real_dates(int month, int day, int? year) =>
        Birthday.IsValid(month, day, year).Should().BeTrue();

    [Theory]
    [InlineData(0, 10, null)]
    [InlineData(13, 10, null)]
    [InlineData(3, 0, null)]
    [InlineData(3, 32, null)]
    [InlineData(4, 31, null)]
    [InlineData(2, 30, null)]
    [InlineData(2, 29, 2023)] // not a leap year.
    [InlineData(2, 29, 1900)] // divisible by 100, not by 400.
    [InlineData(3, 14, 0)]
    [InlineData(3, 14, 10000)]
    [InlineData(3, 14, -5)]
    public void IsValid_rejects_dates_that_do_not_exist(int month, int day, int? year) =>
        Birthday.IsValid(month, day, year).Should().BeFalse();

    [Fact]
    public void TryCreate_returns_the_parts_for_a_real_date()
    {
        Birthday.TryCreate(12, 10, 1815, out var birthday).Should().BeTrue();

        birthday!.Month.Should().Be(12);
        birthday.Day.Should().Be(10);
        birthday.Year.Should().Be(1815);
    }

    [Fact]
    public void TryCreate_returns_nothing_for_an_impossible_date()
    {
        Birthday.TryCreate(4, 31, null, out var birthday).Should().BeFalse();

        birthday.Should().BeNull();
    }

    [Fact]
    public void Create_throws_for_an_impossible_date()
    {
        var act = () => Birthday.Create(2, 30);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Date_is_the_full_date_only_when_the_year_is_known()
    {
        Birthday.Create(12, 10, 1815).Date.Should().Be(new DateOnly(1815, 12, 10));
        Birthday.Create(12, 10).Date.Should().BeNull();
    }

    [Fact]
    public void Birthdays_with_the_same_parts_are_equal()
    {
        Birthday.Create(3, 14, 1990).Should().Be(Birthday.Create(3, 14, 1990));
        Birthday.Create(3, 14).Should().NotBe(Birthday.Create(3, 14, 1990));
    }

    [Fact]
    public void A_person_builds_its_birthday_from_the_three_columns()
    {
        var complete = new Person { BirthdayYear = 1992, BirthdayMonth = 2, BirthdayDay = 29 };
        var yearless = new Person { BirthdayMonth = 3, BirthdayDay = 14 };
        var none = new Person();
        var incomplete = new Person { BirthdayMonth = 3 };

        complete.Birthday.Should().Be(Birthday.Create(2, 29, 1992));
        yearless.Birthday.Should().Be(Birthday.Create(3, 14));
        none.Birthday.Should().BeNull();
        incomplete.Birthday.Should().BeNull("a month without a day is not a birthday");
    }
}
