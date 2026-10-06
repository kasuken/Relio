using Relio.Application.People;
using Relio.Domain;

namespace Relio.Application.Tests.People;

/// <summary>
/// The pure rules a person's profile input must meet (issue #22). They take "today" as a
/// parameter, so none of these tests needs a clock, a database or a signed-in user.
/// </summary>
public class PersonProfileRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    [Fact]
    public void Validate_accepts_only_a_first_name()
    {
        var errors = PersonProfileRules.Validate(new CreatePersonRequest { FirstName = "Ada" }, Today);

        errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Validate_requires_a_first_name(string firstName)
    {
        var errors = PersonProfileRules.Validate(new CreatePersonRequest { FirstName = firstName }, Today);

        errors.Should().Equal(PersonValidationError.FirstNameRequired);
    }

    [Fact]
    public void Validate_requires_a_first_name_when_it_is_null()
    {
        var errors = PersonProfileRules.Validate(new CreatePersonRequest { FirstName = null! }, Today);

        errors.Should().Equal(PersonValidationError.FirstNameRequired);
    }

    public static TheoryData<string, PersonValidationError> TextLimits => new()
    {
        { nameof(IPersonProfileInput.FirstName), PersonValidationError.FirstNameTooLong },
        { nameof(IPersonProfileInput.LastName), PersonValidationError.LastNameTooLong },
        { nameof(IPersonProfileInput.Nickname), PersonValidationError.NicknameTooLong },
        { nameof(IPersonProfileInput.HowWeMet), PersonValidationError.HowWeMetTooLong },
        { nameof(IPersonProfileInput.Details), PersonValidationError.DetailsTooLong },
    };

    [Theory]
    [MemberData(nameof(TextLimits))]
    public void Validate_rejects_text_over_each_limit(string field, PersonValidationError expected)
    {
        var limit = LimitOf(field);

        PersonProfileRules.Validate(WithText(field, new string('a', limit + 1)), Today).Should().Equal(expected);
        PersonProfileRules.Validate(WithText(field, new string('a', limit)), Today).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(TextLimits))]
    public void Validate_measures_length_after_trimming(string field, PersonValidationError expected)
    {
        _ = expected;
        var limit = LimitOf(field);

        // Exactly at the limit once the padding is trimmed away: it will be stored at the limit.
        var padded = "  " + new string('a', limit) + "  ";

        PersonProfileRules.Validate(WithText(field, padded), Today).Should().BeEmpty();
    }

    [Theory]
    [InlineData(10, null)]
    [InlineData(null, 12)]
    public void Validate_rejects_a_day_without_a_month_and_a_month_without_a_day(int? day, int? month)
    {
        var request = new CreatePersonRequest { FirstName = "Ada", BirthdayDay = day, BirthdayMonth = month };

        PersonProfileRules.Validate(request, Today).Should().Equal(PersonValidationError.BirthdayIncomplete);
    }

    [Fact]
    public void Validate_rejects_a_year_without_day_and_month()
    {
        var request = new CreatePersonRequest { FirstName = "Ada", BirthdayYear = 1990 };

        PersonProfileRules.Validate(request, Today).Should().Equal(PersonValidationError.BirthdayYearWithoutDayAndMonth);
    }

    [Theory]
    [InlineData(31, 4, null)]
    [InlineData(30, 2, null)]
    [InlineData(29, 2, 2023)]
    [InlineData(0, 5, null)]
    [InlineData(32, 5, null)]
    [InlineData(10, 13, null)]
    [InlineData(10, 0, 1990)]
    [InlineData(10, 5, 0)]
    [InlineData(10, 5, 10000)]
    public void Validate_rejects_dates_that_do_not_exist(int day, int month, int? year)
    {
        var request = new CreatePersonRequest
        {
            FirstName = "Ada",
            BirthdayDay = day,
            BirthdayMonth = month,
            BirthdayYear = year,
        };

        PersonProfileRules.Validate(request, Today).Should().Equal(PersonValidationError.BirthdayNotARealDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2024)]
    public void Validate_accepts_29_February_without_a_year_and_in_a_leap_year(int? year)
    {
        var request = new CreatePersonRequest { FirstName = "Ada", BirthdayDay = 29, BirthdayMonth = 2, BirthdayYear = year };

        PersonProfileRules.Validate(request, Today).Should().BeEmpty();
    }

    [Fact]
    public void Validate_rejects_a_birthday_after_today()
    {
        var request = new CreatePersonRequest { FirstName = "Ada", BirthdayDay = 7, BirthdayMonth = 10, BirthdayYear = 2026 };

        PersonProfileRules.Validate(request, Today).Should().Equal(PersonValidationError.BirthdayInTheFuture);
    }

    [Fact]
    public void Validate_accepts_today()
    {
        var request = new CreatePersonRequest { FirstName = "Ada", BirthdayDay = 6, BirthdayMonth = 10, BirthdayYear = 2026 };

        PersonProfileRules.Validate(request, Today).Should().BeEmpty();
    }

    [Fact]
    public void Validate_accepts_a_day_and_month_later_in_the_year_when_there_is_no_year()
    {
        // 14 March has not "happened" yet this year, but without a year it is not a date in the future.
        var request = new CreatePersonRequest { FirstName = "Ada", BirthdayDay = 31, BirthdayMonth = 12 };

        PersonProfileRules.Validate(request, Today).Should().BeEmpty();
    }

    [Fact]
    public void Validate_reports_every_broken_rule_in_the_order_the_fields_appear()
    {
        var request = new CreatePersonRequest
        {
            FirstName = "",
            LastName = new string('a', Person.LastNameMaxLength + 1),
            Details = new string('a', Person.DetailsMaxLength + 1),
            BirthdayYear = 1990,
        };

        PersonProfileRules.Validate(request, Today).Should().Equal(
            PersonValidationError.FirstNameRequired,
            PersonValidationError.LastNameTooLong,
            PersonValidationError.DetailsTooLong,
            PersonValidationError.BirthdayYearWithoutDayAndMonth);
    }

    [Fact]
    public void Validate_checks_an_update_the_same_way()
    {
        var update = new UpdatePersonRequest { FirstName = " ", BirthdayDay = 1 };

        PersonProfileRules.Validate(update, Today).Should().Equal(
            PersonValidationError.FirstNameRequired,
            PersonValidationError.BirthdayIncomplete);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  Ada  ", "Ada")]
    public void NormalizeRequired_trims_and_turns_null_into_empty(string? value, string expected) =>
        PersonProfileRules.NormalizeRequired(value).Should().Be(expected);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("\n\t", null)]
    [InlineData("  Ada  ", "Ada")]
    [InlineData("two\nlines", "two\nlines")]
    public void NormalizeOptional_turns_blank_into_null(string? value, string? expected) =>
        PersonProfileRules.NormalizeOptional(value).Should().Be(expected);

    [Fact]
    public void PersonValidationException_lists_codes_and_never_values()
    {
        var exception = new PersonValidationException(
            [PersonValidationError.FirstNameRequired, PersonValidationError.BirthdayIncomplete]);

        exception.Message.Should().Be("The person could not be saved: FirstNameRequired, BirthdayIncomplete.");
        exception.Errors.Should().HaveCount(2);
    }

    private static int LimitOf(string field) => field switch
    {
        nameof(IPersonProfileInput.FirstName) => Person.FirstNameMaxLength,
        nameof(IPersonProfileInput.LastName) => Person.LastNameMaxLength,
        nameof(IPersonProfileInput.Nickname) => Person.NicknameMaxLength,
        nameof(IPersonProfileInput.HowWeMet) => Person.HowWeMetMaxLength,
        nameof(IPersonProfileInput.Details) => Person.DetailsMaxLength,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    private static CreatePersonRequest WithText(string field, string value) => field switch
    {
        nameof(IPersonProfileInput.FirstName) => new CreatePersonRequest { FirstName = value },
        nameof(IPersonProfileInput.LastName) => new CreatePersonRequest { FirstName = "Ada", LastName = value },
        nameof(IPersonProfileInput.Nickname) => new CreatePersonRequest { FirstName = "Ada", Nickname = value },
        nameof(IPersonProfileInput.HowWeMet) => new CreatePersonRequest { FirstName = "Ada", HowWeMet = value },
        nameof(IPersonProfileInput.Details) => new CreatePersonRequest { FirstName = "Ada", Details = value },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };
}
