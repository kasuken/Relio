using Relio.Application.People;
using Relio.Domain;
using Relio.Web.Components.People;

namespace Relio.Web.Tests.People;

public class ContactMethodFormMessagesTests
{
    public static TheoryData<ContactMethodValidationError, ContactMethodKind> EveryErrorForEveryKind
    {
        get
        {
            var data = new TheoryData<ContactMethodValidationError, ContactMethodKind>();
            foreach (var error in Enum.GetValues<ContactMethodValidationError>())
            {
                foreach (var kind in Enum.GetValues<ContactMethodKind>())
                {
                    data.Add(error, kind);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EveryErrorForEveryKind))]
    public void Every_error_has_a_message_for_every_kind(ContactMethodValidationError error, ContactMethodKind kind)
    {
        var (_, message) = ContactMethodFormMessages.For(error, kind);

        message.Should().NotBeNullOrWhiteSpace();
        message.Should().EndWith(".", "messages are full sentences");
        message.Should().NotContain("!", "the design system has no exclamation marks");
        message.ToLowerInvariant().Should().NotContain("sorry", "errors do not apologise");
    }

    [Theory]
    [InlineData(ContactMethodValidationError.KindUnknown, ContactMethodField.Kind, "Choose what kind of contact this is.")]
    [InlineData(ContactMethodValidationError.ValueTooLong, ContactMethodField.Value, "Keep this to 300 characters or fewer.")]
    [InlineData(ContactMethodValidationError.LabelTooLong, ContactMethodField.Label, "Keep the label to 50 characters or fewer.")]
    [InlineData(ContactMethodValidationError.EmailInvalid, ContactMethodField.Value, "Enter an email address like name@example.com.")]
    [InlineData(ContactMethodValidationError.PhoneInvalidCharacters, ContactMethodField.Value, "Use digits, spaces, a leading + and ( ) - . / only.")]
    [InlineData(ContactMethodValidationError.PhoneDigitCount, ContactMethodField.Value, "Enter a phone number with 3 to 15 digits.")]
    public void Each_error_is_shown_under_its_input_with_the_agreed_wording(
        ContactMethodValidationError error, ContactMethodField field, string message)
    {
        ContactMethodFormMessages.For(error, ContactMethodKind.Email).Should().Be((field, message));
    }

    [Theory]
    [InlineData(ContactMethodKind.Email, "Enter an email address, or remove this row.")]
    [InlineData(ContactMethodKind.Phone, "Enter a phone number, or remove this row.")]
    [InlineData(ContactMethodKind.Address, "Enter an address, or remove this row.")]
    [InlineData(ContactMethodKind.Social, "Enter a handle or link, or remove this row.")]
    [InlineData(ContactMethodKind.Other, "Enter the details, or remove this row.")]
    public void A_missing_value_is_worded_for_the_kind_of_row(ContactMethodKind kind, string message)
    {
        ContactMethodFormMessages.For(ContactMethodValidationError.ValueRequired, kind)
            .Should().Be((ContactMethodField.Value, message));
    }

    [Fact]
    public void An_undefined_error_is_refused()
    {
        FluentActions.Invoking(() => ContactMethodFormMessages.For((ContactMethodValidationError)99, ContactMethodKind.Email))
            .Should().Throw<ArgumentOutOfRangeException>();
    }
}
