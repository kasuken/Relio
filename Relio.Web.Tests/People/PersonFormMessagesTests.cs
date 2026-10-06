using Relio.Application.People;
using Relio.Web.Components.People;

namespace Relio.Web.Tests.People;

public class PersonFormMessagesTests
{
    public static TheoryData<PersonValidationError> AllErrors
    {
        get
        {
            var data = new TheoryData<PersonValidationError>();
            foreach (var error in Enum.GetValues<PersonValidationError>())
            {
                data.Add(error);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllErrors))]
    public void Every_validation_error_has_a_message(PersonValidationError error)
    {
        var (_, message) = PersonFormMessages.For(error);

        message.Should().NotBeNullOrWhiteSpace();
        message.Should().EndWith(".", "messages are full sentences");
        message.Should().NotContain("!", "the design system has no exclamation marks");
        message.ToLowerInvariant().Should().NotContain("sorry", "errors do not apologise");
    }

    [Theory]
    [InlineData(PersonValidationError.FirstNameRequired, PersonFormField.FirstName, "Enter a first name.")]
    [InlineData(PersonValidationError.FirstNameTooLong, PersonFormField.FirstName, "Keep the first name to 100 characters or fewer.")]
    [InlineData(PersonValidationError.LastNameTooLong, PersonFormField.LastName, "Keep the last name to 100 characters or fewer.")]
    [InlineData(PersonValidationError.NicknameTooLong, PersonFormField.Nickname, "Keep the nickname to 100 characters or fewer.")]
    [InlineData(PersonValidationError.HowWeMetTooLong, PersonFormField.HowWeMet, "Keep this to 1,000 characters or fewer.")]
    [InlineData(PersonValidationError.DetailsTooLong, PersonFormField.Details, "Keep the details to 4,000 characters or fewer.")]
    [InlineData(PersonValidationError.BirthdayIncomplete, PersonFormField.Birthday, "Choose both a day and a month, or leave the birthday empty.")]
    [InlineData(PersonValidationError.BirthdayYearWithoutDayAndMonth, PersonFormField.Birthday, "Add the day and month too, or clear the year.")]
    [InlineData(PersonValidationError.BirthdayNotARealDate, PersonFormField.Birthday, "That date doesn't exist. Check the day, month and year.")]
    [InlineData(PersonValidationError.BirthdayInTheFuture, PersonFormField.Birthday, "Enter a date in the past or today.")]
    [InlineData(PersonValidationError.TooManyContactMethods, PersonFormField.ContactMethods, "A person can have up to 20 contact methods. Remove one to add another.")]
    [InlineData(PersonValidationError.TooManyTags, PersonFormField.Tags, "A person can have up to 20 tags. Remove one to add another.")]
    [InlineData(PersonValidationError.TagNameTooLong, PersonFormField.Tags, "Keep tag names to 50 characters or fewer.")]
    [InlineData(PersonValidationError.TagNameConflict, PersonFormField.Tags, "A tag with a very similar name already exists. Choose it from the list.")]
    public void Each_error_is_shown_under_its_field_with_the_agreed_wording(
        PersonValidationError error, PersonFormField field, string message)
    {
        PersonFormMessages.For(error).Should().Be((field, message));
    }

    [Fact]
    public void An_unknown_error_is_a_programming_mistake_not_a_silent_blank()
    {
        var act = () => PersonFormMessages.For((PersonValidationError)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
