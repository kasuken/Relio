namespace Relio.Application.People.Import;

/// <summary>
/// Something wrong with one person in an import. Codes, not messages: <c>Relio.Web</c> words them.
/// A <b>blocking</b> problem means the row cannot be imported; every other one is a <b>warning</b>: the
/// offending field is left out (or kept in another form) and the rest of the person is imported.
/// </summary>
public enum ImportProblem
{
    /// <summary>Blocking: there is no first name (or no name at all).</summary>
    NameMissing,

    /// <summary>Blocking: the first name is longer than <c>Person.FirstNameMaxLength</c>.</summary>
    FirstNameTooLong,

    /// <summary>Blocking: the last name is longer than <c>Person.LastNameMaxLength</c>.</summary>
    LastNameTooLong,

    /// <summary>Blocking: something else made the person invalid, which the rules above did not foresee.</summary>
    NotImportable,

    /// <summary>The nickname is too long and is left out.</summary>
    NicknameTooLong,

    /// <summary>The notes are too long and are left out.</summary>
    DetailsTooLong,

    /// <summary>The birthday could not be read and is left out.</summary>
    BirthdayUnreadable,

    /// <summary>The birthday is not a date that exists and is left out.</summary>
    BirthdayNotARealDate,

    /// <summary>The birthday is in the future (in the user's time zone) and is left out.</summary>
    BirthdayInTheFuture,

    /// <summary>There are more contact details than one person can have; the first ones are kept.</summary>
    TooManyContactMethods,

    /// <summary>A contact detail is too long and is left out.</summary>
    ContactValueTooLong,

    /// <summary>An email address did not look right and is kept as other details.</summary>
    EmailKeptAsOther,

    /// <summary>A phone number did not look right and is kept as other details.</summary>
    PhoneKeptAsOther,
}

/// <summary>Which <see cref="ImportProblem"/> codes block a row.</summary>
public static class ImportProblems
{
    /// <summary>Whether <paramref name="problem"/> stops the row being imported.</summary>
    public static bool IsBlocking(ImportProblem problem) =>
        problem is ImportProblem.NameMissing or ImportProblem.FirstNameTooLong or ImportProblem.LastNameTooLong or ImportProblem.NotImportable;
}
