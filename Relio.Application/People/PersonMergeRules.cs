using Relio.Domain;

namespace Relio.Application.People;

/// <summary>
/// How two profiles of the same person become one (issue #28), as pure functions with no database,
/// no current user and no clock. <c>PersonMergeService</c> applies the result and the merge page
/// previews it with the very same <see cref="Combine"/>, so what the user is shown is exactly what
/// is saved, and no default or rule is ever re-implemented in a component.
/// </summary>
/// <remarks>
/// <para>
/// The <i>primary</i> is the profile that stays; the <i>duplicate</i> is merged into it and removed.
/// Every method that takes two <see cref="Person"/> objects needs both with <see cref="Person.Tags"/>
/// and <see cref="Person.ContactMethods"/> loaded; an unloaded collection cannot be told from an
/// empty one, so a missing one would silently merge as "none".
/// </para>
/// <para>
/// <b>Personal data.</b> Everything here is the user's text. Never log it, and never put a part of it
/// in an exception message (the <see cref="ArgumentException"/>s name codes only).
/// </para>
/// </remarks>
public static class PersonMergeRules
{
    /// <summary>The separator <see cref="MergeFieldChoice.Both"/> puts between the two texts: one blank line.</summary>
    public const string KeepBothSeparator = "\n\n";

    /// <summary>
    /// The fields the two profiles disagree on, in <see cref="MergeField"/> order: the ones the user
    /// is asked about. A field where only one side has a value is not a conflict (that value is kept).
    /// </summary>
    public static IReadOnlyList<MergeField> ConflictingFields(Person primary, Person duplicate)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(duplicate);

        var conflicts = new List<MergeField>();

        if (!string.Equals(primary.FirstName, duplicate.FirstName, StringComparison.Ordinal)
            || !string.Equals(primary.LastName, duplicate.LastName, StringComparison.Ordinal))
        {
            conflicts.Add(MergeField.Name);
        }

        if (TextsConflict(primary.Nickname, duplicate.Nickname))
        {
            conflicts.Add(MergeField.Nickname);
        }

        if (primary.RelationshipTypeId is Guid primaryType
            && duplicate.RelationshipTypeId is Guid duplicateType
            && primaryType != duplicateType)
        {
            conflicts.Add(MergeField.RelationshipType);
        }

        if (BirthdaysConflict(primary, duplicate))
        {
            conflicts.Add(MergeField.Birthday);
        }

        if (TextsConflict(primary.HowWeMet, duplicate.HowWeMet))
        {
            conflicts.Add(MergeField.HowWeMet);
        }

        if (TextsConflict(primary.Details, duplicate.Details))
        {
            conflicts.Add(MergeField.Details);
        }

        if (primary.IsArchived != duplicate.IsArchived)
        {
            conflicts.Add(MergeField.ArchivedState);
        }

        return conflicts;
    }

    /// <summary>
    /// What is chosen for <paramref name="field"/> when the user chooses nothing: the primary's value,
    /// except for the archived state, where the side that is <b>not</b> archived wins (merging two
    /// profiles of someone you are in touch with should not hide them).
    /// </summary>
    public static MergeFieldChoice DefaultChoice(MergeField field, Person primary, Person duplicate)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(duplicate);

        if (field == MergeField.ArchivedState)
        {
            return primary.IsArchived && !duplicate.IsArchived ? MergeFieldChoice.Duplicate : MergeFieldChoice.Primary;
        }

        return MergeFieldChoice.Primary;
    }

    /// <summary>
    /// Whether "keep both" is possible for <paramref name="field"/>: only for the two text fields,
    /// when both have text and the two joined by a blank line still fit the field's length limit.
    /// </summary>
    public static bool CanKeepBoth(MergeField field, Person primary, Person duplicate)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(duplicate);

        var (first, second, maxLength) = field switch
        {
            MergeField.HowWeMet => (primary.HowWeMet, duplicate.HowWeMet, Person.HowWeMetMaxLength),
            MergeField.Details => (primary.Details, duplicate.Details, Person.DetailsMaxLength),
            _ => (null, null, 0),
        };

        return first is not null && second is not null && first.Length + KeepBothSeparator.Length + second.Length <= maxLength;
    }

    /// <summary>
    /// Rejects choices no page produces: a field or a choice that is not defined, or
    /// <see cref="MergeFieldChoice.Both"/> for a field that is not text. The message names codes only.
    /// </summary>
    /// <exception cref="ArgumentException">A choice is malformed.</exception>
    public static void ValidateChoices(IReadOnlyDictionary<MergeField, MergeFieldChoice>? choices)
    {
        if (choices is null)
        {
            return;
        }

        foreach (var (field, choice) in choices)
        {
            if (!Enum.IsDefined(field) || !Enum.IsDefined(choice))
            {
                throw new ArgumentException("A merge choice names a field or a side that does not exist.", nameof(choices));
            }

            if (choice == MergeFieldChoice.Both && field is not (MergeField.HowWeMet or MergeField.Details))
            {
                throw new ArgumentException($"'Both' is not a possible choice for {field}.", nameof(choices));
            }
        }
    }

    /// <summary>
    /// Combines <paramref name="duplicate"/> into <paramref name="primary"/> without changing either:
    /// the resolved fields, the united contact methods and tags, and the later last contacted date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A field both profiles have different values for uses <paramref name="choices"/> (or
    /// <see cref="DefaultChoice"/>); a choice for any other field is ignored. A field only one side has
    /// takes that side's value. The birthday is chosen as a whole (day, month and year together), except
    /// that the same day and month with a year on one side only keeps the year. The name is chosen as a
    /// whole too, never first name from one and last name from the other.
    /// </para>
    /// <para>
    /// Contact methods are united and deduplicated by <b>kind and normalized value</b> (the same text
    /// under two kinds is two details): the primary's rows stay in order, then the duplicate's rows the
    /// primary lacks, appended in their own order. A repeat is dropped, but a label the primary's row
    /// lacks is taken from it. Tags are united by id. Nothing is truncated: a union that breaks a limit
    /// is reported by <see cref="PersonProfileRules.Validate"/> on the result.
    /// </para>
    /// </remarks>
    public static MergedProfile Combine(
        Person primary,
        Person duplicate,
        IReadOnlyDictionary<MergeField, MergeFieldChoice>? choices)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(duplicate);
        ValidateChoices(choices);

        var conflicts = ConflictingFields(primary, duplicate);

        MergeFieldChoice ChoiceFor(MergeField field) =>
            choices is not null && choices.TryGetValue(field, out var chosen) ? chosen : DefaultChoice(field, primary, duplicate);

        // The side whose value wins a conflicting field; Both is handled by the text fields themselves.
        Person Winner(MergeField field) => ChoiceFor(field) == MergeFieldChoice.Duplicate ? duplicate : primary;

        var name = conflicts.Contains(MergeField.Name) ? Winner(MergeField.Name) : primary;

        var nickname = ResolveText(MergeField.Nickname, primary.Nickname, duplicate.Nickname);
        var howWeMet = ResolveText(MergeField.HowWeMet, primary.HowWeMet, duplicate.HowWeMet);
        var details = ResolveText(MergeField.Details, primary.Details, duplicate.Details);

        var relationshipTypeId = conflicts.Contains(MergeField.RelationshipType)
            ? Winner(MergeField.RelationshipType).RelationshipTypeId
            : primary.RelationshipTypeId ?? duplicate.RelationshipTypeId;

        var (day, month, year) = ResolveBirthday(primary, duplicate, conflicts.Contains(MergeField.Birthday) ? Winner(MergeField.Birthday) : null);

        var archivedSource = conflicts.Contains(MergeField.ArchivedState) ? Winner(MergeField.ArchivedState) : primary;

        return new MergedProfile
        {
            FirstName = name.FirstName,
            LastName = name.LastName,
            Nickname = nickname,
            RelationshipTypeId = relationshipTypeId,
            BirthdayDay = day,
            BirthdayMonth = month,
            BirthdayYear = year,
            HowWeMet = howWeMet,
            Details = details,
            IsArchived = archivedSource.IsArchived,
            ArchivedAtUtc = archivedSource.ArchivedAtUtc,
            LastContactedOn = Later(primary.LastContactedOn, duplicate.LastContactedOn),
            ContactMethodSteps = CombineContactMethods(primary, duplicate),
            Tags = CombineTags(primary, duplicate),
        };

        string? ResolveText(MergeField field, string? primaryText, string? duplicateText)
        {
            if (!TextsConflict(primaryText, duplicateText))
            {
                return primaryText ?? duplicateText;
            }

            return ChoiceFor(field) switch
            {
                MergeFieldChoice.Duplicate => duplicateText,
                MergeFieldChoice.Both => primaryText + KeepBothSeparator + duplicateText,
                _ => primaryText,
            };
        }
    }

    private static bool TextsConflict(string? primary, string? duplicate) =>
        primary is not null && duplicate is not null && !string.Equals(primary, duplicate, StringComparison.Ordinal);

    private static bool HasBirthday(Person person) => person.BirthdayDay is not null && person.BirthdayMonth is not null;

    private static bool BirthdaysConflict(Person primary, Person duplicate)
    {
        if (!HasBirthday(primary) || !HasBirthday(duplicate))
        {
            return false;
        }

        if (primary.BirthdayDay != duplicate.BirthdayDay || primary.BirthdayMonth != duplicate.BirthdayMonth)
        {
            return true;
        }

        return primary.BirthdayYear is not null && duplicate.BirthdayYear is not null && primary.BirthdayYear != duplicate.BirthdayYear;
    }

    private static (int? Day, int? Month, int? Year) ResolveBirthday(Person primary, Person duplicate, Person? conflictWinner)
    {
        if (conflictWinner is not null)
        {
            return (conflictWinner.BirthdayDay, conflictWinner.BirthdayMonth, conflictWinner.BirthdayYear);
        }

        if (!HasBirthday(primary))
        {
            return HasBirthday(duplicate)
                ? (duplicate.BirthdayDay, duplicate.BirthdayMonth, duplicate.BirthdayYear)
                : (null, null, null);
        }

        // The same day and month, or the duplicate has none: the primary's, plus a year only the duplicate knows.
        var year = primary.BirthdayYear;
        if (year is null && HasBirthday(duplicate))
        {
            year = duplicate.BirthdayYear;
        }

        return (primary.BirthdayDay, primary.BirthdayMonth, year);
    }

    private static DateOnly? Later(DateOnly? first, DateOnly? second) => (first, second) switch
    {
        (null, _) => second,
        (_, null) => first,
        var (a, b) => a >= b ? a : b,
    };

    private static List<ContactMethodMergeStep> CombineContactMethods(Person primary, Person duplicate)
    {
        var steps = new List<ContactMethodMergeStep>();
        var firstPrimaryStep = new Dictionary<(ContactMethodKind, string), int>();

        foreach (var row in primary.ContactMethods.OrderBy(row => row.SortOrder))
        {
            firstPrimaryStep.TryAdd((row.Kind, row.NormalizedValue), steps.Count);
            steps.Add(new ContactMethodMergeStep(row, ContactMethodMergeAction.KeepPrimary, null, row.SortOrder));
        }

        var nextSortOrder = primary.ContactMethods.Count == 0 ? 0 : primary.ContactMethods.Max(row => row.SortOrder) + 1;

        foreach (var row in duplicate.ContactMethods.OrderBy(row => row.SortOrder))
        {
            var key = (row.Kind, row.NormalizedValue);
            if (firstPrimaryStep.TryGetValue(key, out var existingIndex))
            {
                var existing = steps[existingIndex];
                if (existing.Action == ContactMethodMergeAction.KeepPrimary
                    && existing.Row.Label is null
                    && existing.LabelToFill is null
                    && row.Label is not null)
                {
                    steps[existingIndex] = existing with { LabelToFill = row.Label };
                }

                steps.Add(new ContactMethodMergeStep(row, ContactMethodMergeAction.DropDuplicate, null, row.SortOrder));
                continue;
            }

            // Later rows of the duplicate that repeat this one find it among the steps.
            firstPrimaryStep[key] = steps.Count;
            steps.Add(new ContactMethodMergeStep(row, ContactMethodMergeAction.MoveFromDuplicate, null, nextSortOrder++));
        }

        return steps;
    }

    private static List<Tag> CombineTags(Person primary, Person duplicate)
    {
        var tags = primary.Tags.ToList();
        var seen = tags.Select(tag => tag.Id).ToHashSet();
        foreach (var tag in duplicate.Tags)
        {
            if (seen.Add(tag.Id))
            {
                tags.Add(tag);
            }
        }

        return tags;
    }
}
