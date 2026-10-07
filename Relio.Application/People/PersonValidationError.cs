namespace Relio.Application.People;

/// <summary>
/// Why a person's profile input was rejected. Codes, not messages: Application never words copy
/// for the user - <c>Relio.Web</c> maps each code to the text next to the field it belongs to.
/// </summary>
public enum PersonValidationError
{
    /// <summary>The first name is empty or whitespace.</summary>
    FirstNameRequired,

    /// <summary>The first name is longer than <c>Person.FirstNameMaxLength</c>.</summary>
    FirstNameTooLong,

    /// <summary>The last name is longer than <c>Person.LastNameMaxLength</c>.</summary>
    LastNameTooLong,

    /// <summary>The nickname is longer than <c>Person.NicknameMaxLength</c>.</summary>
    NicknameTooLong,

    /// <summary>"How we met" is longer than <c>Person.HowWeMetMaxLength</c>.</summary>
    HowWeMetTooLong,

    /// <summary>The details are longer than <c>Person.DetailsMaxLength</c>.</summary>
    DetailsTooLong,

    /// <summary>A birthday day without a month, or a month without a day.</summary>
    BirthdayIncomplete,

    /// <summary>A birthday year without a day and a month.</summary>
    BirthdayYearWithoutDayAndMonth,

    /// <summary>The day, month and year do not describe a date that exists (31 April, 30 February).</summary>
    BirthdayNotARealDate,

    /// <summary>The birthday, with its year, is after today in the user's time zone.</summary>
    BirthdayInTheFuture,

    /// <summary>More than <c>ContactMethodRules.MaxPerPerson</c> contact methods were submitted.</summary>
    TooManyContactMethods,

    /// <summary>More than <c>TagNameRules.MaxPerPerson</c> distinct tags (existing and new together) were submitted.</summary>
    TooManyTags,

    /// <summary>A new tag name is longer than <c>Tag.NameMaxLength</c>.</summary>
    TagNameTooLong,

    /// <summary>
    /// A new tag name was taken, by a tag whose name the database treats as the same, between
    /// reading the user's tags and saving. Reported only when the unique index refuses the save.
    /// </summary>
    TagNameConflict,
}
