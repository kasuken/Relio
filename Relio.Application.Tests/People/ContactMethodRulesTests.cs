using Relio.Application.People;
using Relio.Domain;

namespace Relio.Application.Tests.People;

/// <summary>
/// The pure rules a contact method must meet and the comparison key issues #27 and #28 will match
/// on (issue #24). No database, no current user, no clock.
/// </summary>
public class ContactMethodRulesTests
{
    private static ContactMethodInput Input(ContactMethodKind kind, string? value, string? label = null) =>
        new(null, kind, label, value);

    [Fact]
    public void NormalizeEmail_trims_and_lower_cases()
    {
        ContactMethodRules.NormalizeEmail("  Ada.Lovelace@Example.COM \t").Should().Be("ada.lovelace@example.com");
        ContactMethodRules.NormalizeEmail(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData("+39 333 123-4567", "+393331234567")]
    [InlineData("(020) 7946 0958", "02079460958")]
    [InlineData(" +1 (555) 010-9999", "+15550109999")]
    [InlineData("0039 333 1234", "00393331234")]
    [InlineData("333.123.4567", "3331234567")]
    public void NormalizePhone_keeps_only_digits_and_a_leading_plus(string value, string expected)
    {
        ContactMethodRules.NormalizePhone(value).Should().Be(expected);
    }

    [Fact]
    public void ToNormalizedValue_for_social_strips_leading_ats_and_lower_cases()
    {
        ContactMethodRules.ToNormalizedValue(ContactMethodKind.Social, "  @Elena.Designs ").Should().Be("elena.designs");
        ContactMethodRules.ToNormalizedValue(ContactMethodKind.Social, "@@Elena").Should().Be("elena");
        ContactMethodRules.ToNormalizedValue(ContactMethodKind.Social, "https://Example.org/Elena  Designs")
            .Should().Be("https://example.org/elena designs");
    }

    [Fact]
    public void ToNormalizedValue_for_address_collapses_whitespace_and_lower_cases()
    {
        ContactMethodRules.ToNormalizedValue(ContactMethodKind.Address, "12  Example Square\r\nLondon\n\n UK ")
            .Should().Be("12 example square london uk");
        ContactMethodRules.ToNormalizedValue(ContactMethodKind.Other, " Ask   Marco ").Should().Be("ask marco");
    }

    [Theory]
    [InlineData(ContactMethodKind.Email, "  Ada@Example.COM ")]
    [InlineData(ContactMethodKind.Phone, " +39 (333) 123-4567 ")]
    [InlineData(ContactMethodKind.Social, " @@Some  Handle ")]
    [InlineData(ContactMethodKind.Address, "12  Example\nSquare ")]
    [InlineData(ContactMethodKind.Other, " Some \r\n note ")]
    public void ToNormalizedValue_is_idempotent_for_every_kind(ContactMethodKind kind, string value)
    {
        var once = ContactMethodRules.ToNormalizedValue(kind, value);

        ContactMethodRules.ToNormalizedValue(kind, once).Should().Be(once);
        once.Length.Should().BeLessThanOrEqualTo(value.Trim().Length, "the key must always fit where the value fits");
    }

    [Fact]
    public void ToNormalizedValue_rejects_an_undefined_kind()
    {
        var act = () => ContactMethodRules.ToNormalizedValue((ContactMethodKind)99, "x");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NormalizeValue_trims_and_unifies_line_breaks()
    {
        ContactMethodRules.NormalizeValue(ContactMethodKind.Address, "  1 Example Square\r\nLondon\rUK \n")
            .Should().Be("1 Example Square\nLondon\nUK");
        ContactMethodRules.NormalizeValue(ContactMethodKind.Email, null).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ContactMethodKind.Email, "ada@example.com")]
    [InlineData(ContactMethodKind.Phone, "+44 7700 900123")]
    [InlineData(ContactMethodKind.Address, "12 Example Square\nLondon")]
    [InlineData(ContactMethodKind.Social, "@elena.designs")]
    [InlineData(ContactMethodKind.Other, "Ask Marco")]
    public void Validate_accepts_a_valid_value_for_every_kind(ContactMethodKind kind, string value)
    {
        ContactMethodRules.Validate(Input(kind, value, "Work")).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ContactMethodKind.Email)]
    [InlineData(ContactMethodKind.Phone)]
    [InlineData(ContactMethodKind.Address)]
    [InlineData(ContactMethodKind.Social)]
    [InlineData(ContactMethodKind.Other)]
    public void Validate_requires_a_value(ContactMethodKind kind)
    {
        ContactMethodRules.Validate(Input(kind, null)).Should().Equal(ContactMethodValidationError.ValueRequired);
        ContactMethodRules.Validate(Input(kind, "  \t\n ")).Should().Equal(ContactMethodValidationError.ValueRequired);
    }

    [Fact]
    public void Validate_measures_value_length_after_trimming()
    {
        var atLimit = new string('a', ContactMethod.ValueMaxLength);

        ContactMethodRules.Validate(Input(ContactMethodKind.Other, "  " + atLimit + "  ")).Should().BeEmpty();
        ContactMethodRules.Validate(Input(ContactMethodKind.Other, atLimit + "a"))
            .Should().Equal(ContactMethodValidationError.ValueTooLong);
    }

    [Fact]
    public void Validate_rejects_a_label_over_50_characters()
    {
        var atLimit = new string('l', ContactMethod.LabelMaxLength);

        ContactMethodRules.Validate(Input(ContactMethodKind.Other, "x", " " + atLimit + " ")).Should().BeEmpty();
        ContactMethodRules.Validate(Input(ContactMethodKind.Other, "x", atLimit + "l"))
            .Should().Equal(ContactMethodValidationError.LabelTooLong);
    }

    [Theory]
    [InlineData("ada")]
    [InlineData("ada@")]
    [InlineData("@example.com")]
    [InlineData("ada@example")]
    [InlineData("Ada <ada@example.com>")]
    [InlineData("ada @example.com")]
    [InlineData("ada@@example.com")]
    [InlineData("ada@.example.com")]
    [InlineData("ada@example.com.")]
    [InlineData("ada@example.com, grace@example.com")]
    public void Validate_rejects_malformed_emails(string value)
    {
        ContactMethodRules.Validate(Input(ContactMethodKind.Email, value))
            .Should().Equal(ContactMethodValidationError.EmailInvalid);
    }

    [Theory]
    [InlineData("ada@example.com")]
    [InlineData("ada.lovelace+news@mail.example.co.uk")]
    [InlineData("A_B-c@Example.ORG")]
    public void Validate_accepts_plausible_emails(string value)
    {
        ContactMethodRules.Validate(Input(ContactMethodKind.Email, value)).Should().BeEmpty();
    }

    [Fact]
    public void Validate_rejects_an_email_longer_than_254_characters()
    {
        var value = new string('a', 64) + "@" + string.Join('.', Enumerable.Repeat(new string('b', 63), 3)) + ".com";

        value.Length.Should().BeInRange(255, ContactMethod.ValueMaxLength);
        ContactMethodRules.Validate(Input(ContactMethodKind.Email, value))
            .Should().Equal(ContactMethodValidationError.EmailInvalid);
    }

    [Theory]
    [InlineData("333-CALL-ADA")]
    [InlineData("+39 333 ext 5")]
    [InlineData("12+34")]
    [InlineData("++39 333")]
    [InlineData("+39 333 1234;")]
    public void Validate_rejects_phone_numbers_with_other_characters(string value)
    {
        ContactMethodRules.Validate(Input(ContactMethodKind.Phone, value))
            .Should().Equal(ContactMethodValidationError.PhoneInvalidCharacters);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("+1")]
    [InlineData("()")]
    [InlineData("1234567890123456")]
    [InlineData("+44 7700 900 123 456 789")]
    public void Validate_rejects_phone_numbers_with_too_few_or_too_many_digits(string value)
    {
        ContactMethodRules.Validate(Input(ContactMethodKind.Phone, value))
            .Should().Equal(ContactMethodValidationError.PhoneDigitCount);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("+393331234567")]
    [InlineData("+39 333 123-4567")]
    [InlineData("(020) 7946 0958")]
    [InlineData("333.123.4567")]
    [InlineData("02/7946 0958")]
    [InlineData("123456789012345")]
    public void Validate_accepts_phone_numbers_in_common_formats(string value)
    {
        ContactMethodRules.Validate(Input(ContactMethodKind.Phone, value)).Should().BeEmpty();
    }

    [Fact]
    public void Validate_rejects_an_unknown_kind_without_judging_anything_else()
    {
        ContactMethodRules.Validate(Input((ContactMethodKind)42, "")).Should().Equal(ContactMethodValidationError.KindUnknown);
    }

    [Fact]
    public void Validate_can_report_a_label_and_a_value_problem_together()
    {
        ContactMethodRules.Validate(Input(ContactMethodKind.Email, "nope", new string('l', 51)))
            .Should().Equal(ContactMethodValidationError.LabelTooLong, ContactMethodValidationError.EmailInvalid);
    }

    [Fact]
    public void ValidateAll_reports_each_problem_with_its_index()
    {
        var problems = ContactMethodRules.ValidateAll(
        [
            Input(ContactMethodKind.Email, "ada@example.com"),
            Input(ContactMethodKind.Email, "not an email"),
            Input(ContactMethodKind.Phone, ""),
            Input(ContactMethodKind.Phone, "+39 333 123 4567"),
        ]);

        problems.Should().Equal(
            new ContactMethodProblem(1, ContactMethodValidationError.EmailInvalid),
            new ContactMethodProblem(2, ContactMethodValidationError.ValueRequired));
    }

    [Fact]
    public void ValidateAll_treats_no_list_as_no_problems()
    {
        ContactMethodRules.ValidateAll(null).Should().BeEmpty();
        ContactMethodRules.ValidateAll([]).Should().BeEmpty();
    }
}
