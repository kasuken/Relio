using Relio.Domain;

namespace Relio.Application.People;

/// <summary>
/// The rules a person's profile input must meet, as pure functions with no database, no current
/// user and no clock: "today" is passed in (see <see cref="Relio.Application.Time.UserCalendar.Today"/>),
/// so tests can exercise them directly. <c>IPeopleService</c> normalizes with these and validates
/// with these before touching the database; the Web layer only ever words the resulting codes.
/// </summary>
public static class PersonProfileRules
{
    /// <summary>Trims required text; null becomes the empty string (which then fails validation).</summary>
    public static string NormalizeRequired(string? value) => value?.Trim() ?? string.Empty;

    /// <summary>Trims optional text; null or blank becomes <see langword="null"/>.</summary>
    public static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Checks <paramref name="input"/> and returns every rule it breaks (empty when it is fine).
    /// Lengths are measured after trimming, the way they will be stored.
    /// </summary>
    /// <param name="input">The profile fields to check.</param>
    /// <param name="today">
    /// Today in the owning user's time zone: a birthday with a year may not be after it.
    /// </param>
    public static IReadOnlyList<PersonValidationError> Validate(IPersonProfileInput input, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(input);

        var errors = new List<PersonValidationError>();

        var firstName = NormalizeRequired(input.FirstName);
        if (firstName.Length == 0)
        {
            errors.Add(PersonValidationError.FirstNameRequired);
        }
        else if (firstName.Length > Person.FirstNameMaxLength)
        {
            errors.Add(PersonValidationError.FirstNameTooLong);
        }

        AddIfTooLong(errors, input.LastName, Person.LastNameMaxLength, PersonValidationError.LastNameTooLong);
        AddIfTooLong(errors, input.Nickname, Person.NicknameMaxLength, PersonValidationError.NicknameTooLong);
        AddIfTooLong(errors, input.HowWeMet, Person.HowWeMetMaxLength, PersonValidationError.HowWeMetTooLong);
        AddIfTooLong(errors, input.Details, Person.DetailsMaxLength, PersonValidationError.DetailsTooLong);

        ValidateBirthday(input, today, errors);
        ValidateContactMethodCount(input, errors);
        ValidateTags(input, errors);

        return errors;
    }

    /// <summary>
    /// The per-row contact method problems in <paramref name="input"/>: what the format rules in
    /// <see cref="ContactMethodRules"/> say about each submitted row. Kept apart from
    /// <see cref="Validate"/> because each problem carries a row index, not just a code.
    /// </summary>
    public static IReadOnlyList<ContactMethodProblem> ValidateContactMethods(IPersonProfileInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ContactMethodRules.ValidateAll(input.ContactMethods);
    }

    private static void ValidateContactMethodCount(IPersonProfileInput input, List<PersonValidationError> errors)
    {
        if (input.ContactMethods?.Count > ContactMethodRules.MaxPerPerson)
        {
            errors.Add(PersonValidationError.TooManyContactMethods);
        }
    }

    private static void ValidateTags(IPersonProfileInput input, List<PersonValidationError> errors)
    {
        var newNames = (input.NewTagNames ?? [])
            .Select(TagNameRules.Normalize)
            .OfType<string>()
            .Distinct(TagNameRules.Comparer)
            .ToList();

        if (newNames.Any(name => name.Length > Tag.NameMaxLength))
        {
            errors.Add(PersonValidationError.TagNameTooLong);
        }

        var tagCount = (input.TagIds?.Distinct().Count() ?? 0) + newNames.Count;
        if (tagCount > TagNameRules.MaxPerPerson)
        {
            errors.Add(PersonValidationError.TooManyTags);
        }
    }

    private static void AddIfTooLong(
        List<PersonValidationError> errors, string? value, int maxLength, PersonValidationError error)
    {
        if (NormalizeRequired(value).Length > maxLength)
        {
            errors.Add(error);
        }
    }

    private static void ValidateBirthday(IPersonProfileInput input, DateOnly today, List<PersonValidationError> errors)
    {
        var day = input.BirthdayDay;
        var month = input.BirthdayMonth;
        var year = input.BirthdayYear;

        if (day is null && month is null)
        {
            if (year is not null)
            {
                errors.Add(PersonValidationError.BirthdayYearWithoutDayAndMonth);
            }

            return;
        }

        if (day is null || month is null)
        {
            errors.Add(PersonValidationError.BirthdayIncomplete);
            return;
        }

        if (!Birthday.TryCreate(month.Value, day.Value, year, out var birthday))
        {
            errors.Add(PersonValidationError.BirthdayNotARealDate);
            return;
        }

        if (birthday.Date is DateOnly date && date > today)
        {
            errors.Add(PersonValidationError.BirthdayInTheFuture);
        }
    }
}
