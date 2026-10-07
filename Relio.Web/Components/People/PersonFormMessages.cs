using Relio.Application.People;

namespace Relio.Web.Components.People;

/// <summary>The part of the person form a validation message is shown under.</summary>
public enum PersonFormField
{
    /// <summary>The first name field.</summary>
    FirstName,

    /// <summary>The last name field.</summary>
    LastName,

    /// <summary>The nickname field.</summary>
    Nickname,

    /// <summary>The day, month and year fields as one group.</summary>
    Birthday,

    /// <summary>The "how you met" field.</summary>
    HowWeMet,

    /// <summary>The details field.</summary>
    Details,

    /// <summary>The contact methods group as a whole (a message that is not about one row).</summary>
    ContactMethods,

    /// <summary>The tag picker.</summary>
    Tags,
}

/// <summary>
/// Words the <see cref="PersonValidationError"/> codes Application reports: which field each one
/// belongs under, and what it says. Messages follow the design system - they say what happened and
/// how to fix it, without apologising.
/// </summary>
public static class PersonFormMessages
{
    /// <summary>Returns the field and the message for <paramref name="error"/>.</summary>
    public static (PersonFormField Field, string Message) For(PersonValidationError error) => error switch
    {
        PersonValidationError.FirstNameRequired => (PersonFormField.FirstName, "Enter a first name."),
        PersonValidationError.FirstNameTooLong => (PersonFormField.FirstName, "Keep the first name to 100 characters or fewer."),
        PersonValidationError.LastNameTooLong => (PersonFormField.LastName, "Keep the last name to 100 characters or fewer."),
        PersonValidationError.NicknameTooLong => (PersonFormField.Nickname, "Keep the nickname to 100 characters or fewer."),
        PersonValidationError.HowWeMetTooLong => (PersonFormField.HowWeMet, "Keep this to 1,000 characters or fewer."),
        PersonValidationError.DetailsTooLong => (PersonFormField.Details, "Keep the details to 4,000 characters or fewer."),
        PersonValidationError.BirthdayIncomplete => (PersonFormField.Birthday, "Choose both a day and a month, or leave the birthday empty."),
        PersonValidationError.BirthdayYearWithoutDayAndMonth => (PersonFormField.Birthday, "Add the day and month too, or clear the year."),
        PersonValidationError.BirthdayNotARealDate => (PersonFormField.Birthday, "That date doesn't exist. Check the day, month and year."),
        PersonValidationError.BirthdayInTheFuture => (PersonFormField.Birthday, "Enter a date in the past or today."),
        PersonValidationError.TooManyContactMethods => (PersonFormField.ContactMethods, "A person can have up to 20 contact methods. Remove one to add another."),
        PersonValidationError.TooManyTags => (PersonFormField.Tags, "A person can have up to 20 tags. Remove one to add another."),
        PersonValidationError.TagNameTooLong => (PersonFormField.Tags, "Keep tag names to 50 characters or fewer."),
        PersonValidationError.TagNameConflict => (PersonFormField.Tags, "A tag with a very similar name already exists. Choose it from the list."),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "No message is defined for this validation error."),
    };
}
