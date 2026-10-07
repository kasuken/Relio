using Relio.Domain;

namespace Relio.Application.People.Import;

/// <summary>
/// Turns what the parsers read into the import preview: each person is cleaned with the same
/// <see cref="PersonProfileRules"/> and <see cref="ContactMethodRules"/> the person form uses, and
/// compared with the user's existing people and with the earlier rows of the same file through
/// <see cref="PossibleDuplicateMatcher"/>. Pure and synchronous: the data layer loads the existing
/// people (<c>PeopleImportService.PreviewAsync</c>); "today" is a parameter, so there is no clock.
/// </summary>
/// <remarks>
/// <para>
/// <b>Blocking problems and warnings.</b> A person with no usable first name, or a first or last name
/// that is too long, cannot be imported (<see cref="ImportProblems.IsBlocking"/>); the row is shown, disabled.
/// Everything else is a warning: the offending field is dropped (nickname, notes, birthday, a contact
/// detail) or kept in another form (an email or phone number that fails the format check becomes an
/// "other" detail labelled "Email" or "Phone"), so one bad cell never costs a whole contact. The cleaning
/// loop runs the real rules until they report nothing, so every importable <see cref="ImportPersonRequest"/>
/// passes validation, and <c>PeopleImportService.ImportAsync</c> re-checks it anyway.
/// </para>
/// <para>
/// <b>Cost.</b> Each row is matched against the existing people and against the earlier rows, so the file
/// costs up to rows x (existing people + rows) bounded comparisons; at the limits (2,000 rows) that is
/// a few million cheap string comparisons, under a second. The existing people are prepared once.
/// </para>
/// <para><b>Personal data.</b> Nothing here is stored or logged.</para>
/// </remarks>
public static class ImportCandidateBuilder
{
    private const int MaxDisplayNameLength = 80;
    private const int MaxCleaningRounds = 6;

    /// <summary>Builds the preview for <paramref name="read"/>.</summary>
    /// <param name="read">What the parser found.</param>
    /// <param name="today">Today in the user's time zone (a birthday may not be after it).</param>
    /// <param name="existing">The user's existing people, prepared once with <see cref="PossibleDuplicateMatcher.Prepare"/>.</param>
    public static ImportPreview Build(ImportReadResult read, DateOnly today, PreparedCandidates existing)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(existing);

        var candidates = new List<ImportCandidate>(read.People.Count);

        // The earlier rows of this file, matched like existing people. A live list behind one wrapper:
        // each row is matched before it is added, so a row is only ever compared with the rows above it.
        var earlier = new List<PreparedCandidate>();
        var earlierRows = new PreparedCandidates(earlier);
        var rowNumbers = new Dictionary<Guid, int>();

        foreach (var draft in read.People)
        {
            var (request, problems, displayName) = BuildOne(draft, today);
            var rowId = Guid.NewGuid();
            IReadOnlyList<PossibleDuplicate> existingMatches = [];
            int? sameAs = null;

            if (request is not null)
            {
                var probe = DuplicateProbe.Create(PossibleDuplicateQuery.FromProfile(request));
                if (probe.First.Length > 0 || probe.EmailKeys.Count > 0 || probe.PhoneKeys.Count > 0)
                {
                    existingMatches = PossibleDuplicateMatcher.Find(probe, existing);

                    var repeats = PossibleDuplicateMatcher.Find(probe, earlierRows);
                    if (repeats.Count > 0)
                    {
                        sameAs = repeats.Min(match => rowNumbers[match.Id]);
                    }
                }

                var asCandidate = new DuplicateCandidate(rowId, request.FirstName, request.LastName, request.Nickname, false, probe.EmailKeys.ToList(), probe.PhoneKeys.ToList());
                earlier.Add(PossibleDuplicateMatcher.Prepare([asCandidate]).Items[0]);
                rowNumbers[rowId] = draft.RowNumber;
            }

            candidates.Add(new ImportCandidate(draft.RowNumber, rowId, displayName, request, problems, existingMatches, sameAs));
        }

        return new ImportPreview(candidates, read.TotalFound, read.Truncated, read.SkippedEmpty);
    }

    private static (ImportPersonRequest? Request, IReadOnlyList<ImportProblem> Problems, string DisplayName) BuildOne(ImportPersonDraft draft, DateOnly today)
    {
        var first = PersonProfileRules.NormalizeRequired(draft.FirstName);
        var last = PersonProfileRules.NormalizeOptional(draft.LastName);

        var blocking = new List<ImportProblem>();
        if (first.Length == 0)
        {
            blocking.Add(ImportProblem.NameMissing);
        }
        else if (first.Length > Person.FirstNameMaxLength)
        {
            blocking.Add(ImportProblem.FirstNameTooLong);
        }

        if (last?.Length > Person.LastNameMaxLength)
        {
            blocking.Add(ImportProblem.LastNameTooLong);
        }

        if (blocking.Count > 0)
        {
            return (null, blocking, Truncate(first.Length > 0 || last is not null ? Person.FormatDisplayName(first, last).Trim() : string.Empty));
        }

        var warnings = new List<ImportProblem>();
        var nickname = PersonProfileRules.NormalizeOptional(draft.Nickname);
        var details = PersonProfileRules.NormalizeOptional(draft.Details);
        var birthday = draft.Birthday;
        if (draft.BirthdayUnreadable)
        {
            warnings.Add(ImportProblem.BirthdayUnreadable);
        }

        var contacts = new List<ContactMethodInput>();
        foreach (var contact in draft.ContactMethods)
        {
            var value = ContactMethodRules.NormalizeValue(contact.Kind, contact.Value);
            if (value.Length > 0)
            {
                contacts.Add(new ContactMethodInput(null, contact.Kind, ContactMethodRules.NormalizeLabel(contact.Label), value));
            }
        }

        if (contacts.Count > ContactMethodRules.MaxPerPerson)
        {
            warnings.Add(ImportProblem.TooManyContactMethods);
            contacts = contacts.Take(ContactMethodRules.MaxPerPerson).ToList();
        }

        // Clean until the real rules report nothing. Each round removes at least one offending field, so
        // it ends; the round limit is a backstop that blocks the row instead of looping.
        for (var round = 0; round <= MaxCleaningRounds; round++)
        {
            var request = new ImportPersonRequest
            {
                FirstName = first,
                LastName = last,
                Nickname = nickname,
                BirthdayMonth = birthday?.Month,
                BirthdayDay = birthday?.Day,
                BirthdayYear = birthday?.Year,
                Details = details,
                ContactMethods = contacts,
            };

            var errors = PersonProfileRules.Validate(request, today);
            var rowProblems = PersonProfileRules.ValidateContactMethods(request);
            if (errors.Count == 0 && rowProblems.Count == 0)
            {
                return (request, Distinct(warnings), Person.FormatDisplayName(first, last));
            }

            foreach (var error in errors)
            {
                switch (error)
                {
                    case PersonValidationError.NicknameTooLong:
                        nickname = null;
                        warnings.Add(ImportProblem.NicknameTooLong);
                        break;
                    case PersonValidationError.DetailsTooLong:
                        details = null;
                        warnings.Add(ImportProblem.DetailsTooLong);
                        break;
                    case PersonValidationError.BirthdayInTheFuture:
                        birthday = null;
                        warnings.Add(ImportProblem.BirthdayInTheFuture);
                        break;
                    case PersonValidationError.BirthdayNotARealDate or PersonValidationError.BirthdayIncomplete or PersonValidationError.BirthdayYearWithoutDayAndMonth:
                        birthday = null;
                        warnings.Add(ImportProblem.BirthdayNotARealDate);
                        break;
                    case PersonValidationError.TooManyContactMethods:
                        contacts = contacts.Take(ContactMethodRules.MaxPerPerson).ToList();
                        warnings.Add(ImportProblem.TooManyContactMethods);
                        break;
                    default:
                        return (null, [ImportProblem.NotImportable], Truncate(Person.FormatDisplayName(first, last)));
                }
            }

            if (rowProblems.Count > 0)
            {
                contacts = FixContacts(contacts, rowProblems, warnings);
            }
        }

        return (null, [ImportProblem.NotImportable], Truncate(Person.FormatDisplayName(first, last)));
    }

    private static List<ContactMethodInput> FixContacts(List<ContactMethodInput> contacts, IReadOnlyList<ContactMethodProblem> problems, List<ImportProblem> warnings)
    {
        var fixedContacts = new List<ContactMethodInput>(contacts.Count);
        for (var index = 0; index < contacts.Count; index++)
        {
            var contact = contacts[index];
            var errors = problems.Where(problem => problem.Index == index).Select(problem => problem.Error).ToList();
            if (errors.Count == 0)
            {
                fixedContacts.Add(contact);
                continue;
            }

            if (errors.Contains(ContactMethodValidationError.ValueTooLong))
            {
                warnings.Add(ImportProblem.ContactValueTooLong);
                continue;
            }

            if (errors.Contains(ContactMethodValidationError.ValueRequired) || errors.Contains(ContactMethodValidationError.KindUnknown))
            {
                continue;
            }

            if (errors.Contains(ContactMethodValidationError.EmailInvalid))
            {
                warnings.Add(ImportProblem.EmailKeptAsOther);
                fixedContacts.Add(contact with { Kind = ContactMethodKind.Other, Label = OtherLabel("Email", contact.Label) });
                continue;
            }

            if (errors.Contains(ContactMethodValidationError.PhoneInvalidCharacters) || errors.Contains(ContactMethodValidationError.PhoneDigitCount))
            {
                warnings.Add(ImportProblem.PhoneKeptAsOther);
                fixedContacts.Add(contact with { Kind = ContactMethodKind.Other, Label = OtherLabel("Phone", contact.Label) });
                continue;
            }

            // Only a label that is too long is left.
            fixedContacts.Add(contact with { Label = null });
        }

        return fixedContacts;
    }

    /// <summary>"Email · Work"; just "Email" when the original label is missing or the result would not fit.</summary>
    private static string OtherLabel(string what, string? original)
    {
        if (string.IsNullOrWhiteSpace(original))
        {
            return what;
        }

        var combined = $"{what} · {original}";
        return combined.Length > ContactMethod.LabelMaxLength ? what : combined;
    }

    private static List<ImportProblem> Distinct(List<ImportProblem> problems) => problems.Distinct().ToList();

    private static string Truncate(string name) =>
        name.Length <= MaxDisplayNameLength ? name : name[..(MaxDisplayNameLength - 1)] + "…";
}
