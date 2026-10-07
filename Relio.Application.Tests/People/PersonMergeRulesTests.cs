using Relio.Application.People;
using Relio.Domain;

namespace Relio.Application.Tests.People;

/// <summary>How two profiles become one (issue #28): conflicts, defaults, combining. No database.</summary>
public class PersonMergeRulesTests
{
    private static readonly DateTime Archived = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    private static Person NewPerson(string first = "Ada", string? last = "Lovelace") =>
        new() { OwnerId = "owner-1", FirstName = first, LastName = last };

    private static ContactMethod Contact(ContactMethodKind kind, string value, int sortOrder, string? label = null) =>
        new()
        {
            OwnerId = "owner-1",
            Kind = kind,
            Label = label,
            Value = value,
            NormalizedValue = ContactMethodRules.ToNormalizedValue(kind, value),
            SortOrder = sortOrder,
        };

    private static Dictionary<MergeField, MergeFieldChoice> Choose(MergeField field, MergeFieldChoice choice) =>
        new() { [field] = choice };

    // ---- Conflicts and defaults ------------------------------------------------------------

    [Fact]
    public void ConflictingFields_lists_only_fields_where_both_have_different_values()
    {
        var typeA = Guid.NewGuid();
        var primary = NewPerson();
        primary.Nickname = "Countess";
        primary.RelationshipTypeId = typeA;
        primary.HowWeMet = "Conference";
        var duplicate = NewPerson();
        duplicate.Nickname = null;
        duplicate.RelationshipTypeId = Guid.NewGuid();
        duplicate.HowWeMet = "Conference";
        duplicate.Details = "Likes chess";

        var conflicts = PersonMergeRules.ConflictingFields(primary, duplicate);

        conflicts.Should().Equal(MergeField.RelationshipType);
    }

    [Fact]
    public void ConflictingFields_in_MergeField_order_for_everything_that_differs()
    {
        var primary = NewPerson("Ada", "Lovelace");
        primary.Nickname = "A";
        primary.RelationshipTypeId = Guid.NewGuid();
        primary.BirthdayDay = 10;
        primary.BirthdayMonth = 12;
        primary.HowWeMet = "x";
        primary.Details = "x";
        var duplicate = NewPerson("Ada", "Byron");
        duplicate.Nickname = "B";
        duplicate.RelationshipTypeId = Guid.NewGuid();
        duplicate.BirthdayDay = 11;
        duplicate.BirthdayMonth = 12;
        duplicate.HowWeMet = "y";
        duplicate.Details = "y";
        duplicate.IsArchived = true;

        PersonMergeRules.ConflictingFields(primary, duplicate).Should().Equal(
            MergeField.Name,
            MergeField.Nickname,
            MergeField.RelationshipType,
            MergeField.Birthday,
            MergeField.HowWeMet,
            MergeField.Details,
            MergeField.ArchivedState);
    }

    [Fact]
    public void A_name_that_differs_only_in_case_is_a_conflict()
    {
        PersonMergeRules.ConflictingFields(NewPerson("ada", "lovelace"), NewPerson("Ada", "Lovelace"))
            .Should().Equal(MergeField.Name);
    }

    [Fact]
    public void Name_is_one_choice_for_first_and_last_together()
    {
        var primary = NewPerson("Jon", "Smith");
        var duplicate = NewPerson("John", "Smythe");

        var merged = PersonMergeRules.Combine(primary, duplicate, Choose(MergeField.Name, MergeFieldChoice.Duplicate));

        (merged.FirstName, merged.LastName).Should().Be(("John", "Smythe"));

        var kept = PersonMergeRules.Combine(primary, duplicate, null);
        (kept.FirstName, kept.LastName).Should().Be(("Jon", "Smith"));
    }

    [Fact]
    public void A_birthday_with_and_without_a_year_on_the_same_day_is_not_a_conflict_and_keeps_the_year()
    {
        var withoutYear = NewPerson();
        withoutYear.BirthdayDay = 10;
        withoutYear.BirthdayMonth = 12;
        var withYear = NewPerson();
        withYear.BirthdayDay = 10;
        withYear.BirthdayMonth = 12;
        withYear.BirthdayYear = 1815;

        PersonMergeRules.ConflictingFields(withoutYear, withYear).Should().BeEmpty();
        PersonMergeRules.ConflictingFields(withYear, withoutYear).Should().BeEmpty();

        var primaryWithoutYear = PersonMergeRules.Combine(withoutYear, withYear, null);
        (primaryWithoutYear.BirthdayDay, primaryWithoutYear.BirthdayMonth, primaryWithoutYear.BirthdayYear).Should().Be((10, 12, 1815));

        var primaryWithYear = PersonMergeRules.Combine(withYear, withoutYear, null);
        (primaryWithYear.BirthdayDay, primaryWithYear.BirthdayMonth, primaryWithYear.BirthdayYear).Should().Be((10, 12, 1815));
    }

    [Fact]
    public void Different_birthdays_or_different_years_are_a_conflict()
    {
        Person Born(int day, int month, int? year)
        {
            var person = NewPerson();
            (person.BirthdayDay, person.BirthdayMonth, person.BirthdayYear) = (day, month, year);
            return person;
        }

        PersonMergeRules.ConflictingFields(Born(10, 12, null), Born(11, 12, null)).Should().Equal(MergeField.Birthday);
        PersonMergeRules.ConflictingFields(Born(10, 12, null), Born(10, 11, null)).Should().Equal(MergeField.Birthday);
        PersonMergeRules.ConflictingFields(Born(10, 12, 1815), Born(10, 12, 1816)).Should().Equal(MergeField.Birthday);
    }

    [Fact]
    public void A_birthday_is_chosen_as_a_whole()
    {
        var primary = NewPerson();
        (primary.BirthdayDay, primary.BirthdayMonth, primary.BirthdayYear) = (10, 12, null);
        var duplicate = NewPerson();
        (duplicate.BirthdayDay, duplicate.BirthdayMonth, duplicate.BirthdayYear) = (11, 11, 1815);

        var merged = PersonMergeRules.Combine(primary, duplicate, Choose(MergeField.Birthday, MergeFieldChoice.Duplicate));

        (merged.BirthdayDay, merged.BirthdayMonth, merged.BirthdayYear).Should().Be((11, 11, 1815));
    }

    [Fact]
    public void DefaultChoice_keeps_the_primary_except_status_which_keeps_the_active_side()
    {
        var active = NewPerson();
        var archived = NewPerson();
        archived.IsArchived = true;

        PersonMergeRules.DefaultChoice(MergeField.Name, active, archived).Should().Be(MergeFieldChoice.Primary);
        PersonMergeRules.DefaultChoice(MergeField.Details, archived, active).Should().Be(MergeFieldChoice.Primary);
        PersonMergeRules.DefaultChoice(MergeField.ArchivedState, active, archived).Should().Be(MergeFieldChoice.Primary);
        PersonMergeRules.DefaultChoice(MergeField.ArchivedState, archived, active).Should().Be(MergeFieldChoice.Duplicate);
    }

    // ---- Combining fields ------------------------------------------------------------------

    [Theory]
    [InlineData(MergeField.Nickname, MergeFieldChoice.Primary, "Countess")]
    [InlineData(MergeField.Nickname, MergeFieldChoice.Duplicate, "Ada B")]
    [InlineData(MergeField.HowWeMet, MergeFieldChoice.Primary, "Conference")]
    [InlineData(MergeField.HowWeMet, MergeFieldChoice.Duplicate, "Chess club")]
    [InlineData(MergeField.Details, MergeFieldChoice.Primary, "Mathematician")]
    [InlineData(MergeField.Details, MergeFieldChoice.Duplicate, "Programmer")]
    public void Combine_applies_each_text_choice(MergeField field, MergeFieldChoice choice, string expected)
    {
        var primary = NewPerson();
        (primary.Nickname, primary.HowWeMet, primary.Details) = ("Countess", "Conference", "Mathematician");
        var duplicate = NewPerson();
        (duplicate.Nickname, duplicate.HowWeMet, duplicate.Details) = ("Ada B", "Chess club", "Programmer");

        var merged = PersonMergeRules.Combine(primary, duplicate, Choose(field, choice));

        var actual = field switch
        {
            MergeField.Nickname => merged.Nickname,
            MergeField.HowWeMet => merged.HowWeMet,
            _ => merged.Details,
        };
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData(MergeFieldChoice.Primary, false)]
    [InlineData(MergeFieldChoice.Duplicate, true)]
    public void Combine_applies_the_relationship_type_choice(MergeFieldChoice choice, bool expectDuplicate)
    {
        var primary = NewPerson();
        primary.RelationshipTypeId = Guid.NewGuid();
        var duplicate = NewPerson();
        duplicate.RelationshipTypeId = Guid.NewGuid();

        var merged = PersonMergeRules.Combine(primary, duplicate, Choose(MergeField.RelationshipType, choice));

        merged.RelationshipTypeId.Should().Be(expectDuplicate ? duplicate.RelationshipTypeId : primary.RelationshipTypeId);
    }

    [Fact]
    public void Combine_fills_missing_values_from_the_duplicate()
    {
        var typeId = Guid.NewGuid();
        var primary = NewPerson();
        var duplicate = NewPerson();
        duplicate.Nickname = "Countess";
        duplicate.RelationshipTypeId = typeId;
        (duplicate.BirthdayDay, duplicate.BirthdayMonth, duplicate.BirthdayYear) = (10, 12, 1815);
        duplicate.HowWeMet = "Conference";
        duplicate.Details = "Mathematician";

        var merged = PersonMergeRules.Combine(primary, duplicate, null);

        merged.Nickname.Should().Be("Countess");
        merged.RelationshipTypeId.Should().Be(typeId);
        (merged.BirthdayDay, merged.BirthdayMonth, merged.BirthdayYear).Should().Be((10, 12, 1815));
        merged.HowWeMet.Should().Be("Conference");
        merged.Details.Should().Be("Mathematician");
    }

    [Fact]
    public void Combine_keeps_the_primarys_values_when_the_duplicate_has_none()
    {
        var primary = NewPerson();
        primary.Nickname = "Countess";
        (primary.BirthdayDay, primary.BirthdayMonth, primary.BirthdayYear) = (10, 12, null);

        var merged = PersonMergeRules.Combine(primary, NewPerson(), null);

        merged.Nickname.Should().Be("Countess");
        (merged.BirthdayDay, merged.BirthdayMonth, merged.BirthdayYear).Should().Be((10, 12, null));
    }

    [Fact]
    public void Keep_both_joins_the_texts_with_a_blank_line_primary_first()
    {
        var primary = NewPerson();
        (primary.HowWeMet, primary.Details) = ("Conference", "Mathematician");
        var duplicate = NewPerson();
        (duplicate.HowWeMet, duplicate.Details) = ("Chess club", "Programmer");

        PersonMergeRules.Combine(primary, duplicate, Choose(MergeField.HowWeMet, MergeFieldChoice.Both)).HowWeMet
            .Should().Be("Conference\n\nChess club");
        PersonMergeRules.Combine(primary, duplicate, Choose(MergeField.Details, MergeFieldChoice.Both)).Details
            .Should().Be("Mathematician\n\nProgrammer");
    }

    [Fact]
    public void Combine_never_truncates_a_kept_both_text()
    {
        var primary = NewPerson();
        primary.Details = new string('a', Person.DetailsMaxLength);
        var duplicate = NewPerson();
        duplicate.Details = "more";

        var merged = PersonMergeRules.Combine(primary, duplicate, Choose(MergeField.Details, MergeFieldChoice.Both));

        merged.Details!.Length.Should().Be(Person.DetailsMaxLength + 2 + 4);
        PersonProfileRules.Validate(merged, new DateOnly(2026, 10, 6)).Should().Contain(PersonValidationError.DetailsTooLong);
    }

    [Fact]
    public void CanKeepBoth_is_false_when_the_joined_text_would_be_too_long_and_for_non_text_fields()
    {
        var primary = NewPerson();
        (primary.HowWeMet, primary.Details, primary.Nickname) = ("a", new string('a', Person.DetailsMaxLength - 5), "n");
        var duplicate = NewPerson();
        (duplicate.HowWeMet, duplicate.Details, duplicate.Nickname) = ("b", "bbbb", "m");

        PersonMergeRules.CanKeepBoth(MergeField.HowWeMet, primary, duplicate).Should().BeTrue();
        PersonMergeRules.CanKeepBoth(MergeField.Details, primary, duplicate).Should().BeFalse("4000 - 5 + 2 + 4 is over the limit");
        PersonMergeRules.CanKeepBoth(MergeField.Nickname, primary, duplicate).Should().BeFalse();
        PersonMergeRules.CanKeepBoth(MergeField.Name, primary, duplicate).Should().BeFalse();

        duplicate.Details = "bbb";
        PersonMergeRules.CanKeepBoth(MergeField.Details, primary, duplicate).Should().BeTrue("exactly at the limit still fits");

        duplicate.HowWeMet = null;
        PersonMergeRules.CanKeepBoth(MergeField.HowWeMet, primary, duplicate).Should().BeFalse("there is nothing to keep both of");
    }

    public static TheoryData<DateOnly?, DateOnly?, DateOnly?> LastContactedCases() => new()
    {
        { null, null, null },
        { new DateOnly(2026, 5, 1), null, new DateOnly(2026, 5, 1) },
        { null, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 1) },
        { new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 1) },
        { new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1) },
    };

    [Theory]
    [MemberData(nameof(LastContactedCases))]
    public void LastContactedOn_takes_the_later_date(DateOnly? primaryDate, DateOnly? duplicateDate, DateOnly? expected)
    {
        var primary = NewPerson();
        primary.LastContactedOn = primaryDate;
        var duplicate = NewPerson();
        duplicate.LastContactedOn = duplicateDate;

        PersonMergeRules.Combine(primary, duplicate, null).LastContactedOn.Should().Be(expected);
    }

    // ---- Archived state --------------------------------------------------------------------

    [Fact]
    public void Both_active_stays_active_and_both_archived_keeps_the_primarys_archive_time()
    {
        PersonMergeRules.Combine(NewPerson(), NewPerson(), null).IsArchived.Should().BeFalse();

        var primary = NewPerson();
        (primary.IsArchived, primary.ArchivedAtUtc) = (true, Archived);
        var duplicate = NewPerson();
        (duplicate.IsArchived, duplicate.ArchivedAtUtc) = (true, Archived.AddDays(30));

        var merged = PersonMergeRules.Combine(primary, duplicate, null);

        merged.IsArchived.Should().BeTrue();
        merged.ArchivedAtUtc.Should().Be(Archived);
        PersonMergeRules.ConflictingFields(primary, duplicate).Should().NotContain(MergeField.ArchivedState);
    }

    [Fact]
    public void Choosing_the_archived_side_copies_its_archive_time_and_choosing_active_clears_it()
    {
        var active = NewPerson();
        var archived = NewPerson();
        (archived.IsArchived, archived.ArchivedAtUtc) = (true, Archived);

        // Default: the active side wins whichever way round they are.
        PersonMergeRules.Combine(active, archived, null).IsArchived.Should().BeFalse();
        PersonMergeRules.Combine(archived, active, null).IsArchived.Should().BeFalse();
        PersonMergeRules.Combine(archived, active, null).ArchivedAtUtc.Should().BeNull();

        var keepArchived = PersonMergeRules.Combine(active, archived, Choose(MergeField.ArchivedState, MergeFieldChoice.Duplicate));
        keepArchived.IsArchived.Should().BeTrue();
        keepArchived.ArchivedAtUtc.Should().Be(Archived);

        var keepPrimaryArchived = PersonMergeRules.Combine(archived, active, Choose(MergeField.ArchivedState, MergeFieldChoice.Primary));
        keepPrimaryArchived.IsArchived.Should().BeTrue();
        keepPrimaryArchived.ArchivedAtUtc.Should().Be(Archived);
    }

    // ---- Contact methods -------------------------------------------------------------------

    [Fact]
    public void Contact_methods_are_unioned_and_deduped_by_kind_and_normalized_value()
    {
        var primary = NewPerson();
        primary.ContactMethods.Add(Contact(ContactMethodKind.Email, "Ada@Example.com", 0));
        primary.ContactMethods.Add(Contact(ContactMethodKind.Phone, "+44 7700 900123", 1));
        var duplicate = NewPerson();
        duplicate.ContactMethods.Add(Contact(ContactMethodKind.Email, "ada@example.com", 0));
        duplicate.ContactMethods.Add(Contact(ContactMethodKind.Phone, "+447700900123", 1));
        duplicate.ContactMethods.Add(Contact(ContactMethodKind.Email, "countess@example.com", 2));

        var merged = PersonMergeRules.Combine(primary, duplicate, null);

        merged.ContactMethodSteps.Select(s => s.Action).Should().Equal(
            ContactMethodMergeAction.KeepPrimary,
            ContactMethodMergeAction.KeepPrimary,
            ContactMethodMergeAction.DropDuplicate,
            ContactMethodMergeAction.DropDuplicate,
            ContactMethodMergeAction.MoveFromDuplicate);
        merged.ContactMethods!.Select(c => c.Value).Should().Equal("Ada@Example.com", "+44 7700 900123", "countess@example.com");
    }

    [Fact]
    public void The_same_text_under_different_kinds_is_kept_twice()
    {
        var primary = NewPerson();
        primary.ContactMethods.Add(Contact(ContactMethodKind.Other, "ada", 0));
        var duplicate = NewPerson();
        duplicate.ContactMethods.Add(Contact(ContactMethodKind.Social, "ada", 0));

        var merged = PersonMergeRules.Combine(primary, duplicate, null);

        merged.ContactMethodSteps.Select(s => s.Action).Should().Equal(
            ContactMethodMergeAction.KeepPrimary,
            ContactMethodMergeAction.MoveFromDuplicate);
    }

    [Fact]
    public void A_kept_primary_row_without_a_label_takes_the_duplicates_label()
    {
        var primary = NewPerson();
        primary.ContactMethods.Add(Contact(ContactMethodKind.Email, "ada@example.com", 0));
        primary.ContactMethods.Add(Contact(ContactMethodKind.Phone, "+447700900123", 1, "Mobile"));
        var duplicate = NewPerson();
        duplicate.ContactMethods.Add(Contact(ContactMethodKind.Email, "ada@example.com", 0, "Work"));
        duplicate.ContactMethods.Add(Contact(ContactMethodKind.Phone, "+447700900123", 1, "Home"));

        var merged = PersonMergeRules.Combine(primary, duplicate, null);

        merged.ContactMethodSteps[0].LabelToFill.Should().Be("Work");
        merged.ContactMethodSteps[1].LabelToFill.Should().BeNull("the primary already had a label");
        merged.ContactMethods!.Select(c => c.Label).Should().Equal("Work", "Mobile");
    }

    [Fact]
    public void Moved_rows_follow_the_primarys_rows_in_their_own_order()
    {
        var primary = NewPerson();
        primary.ContactMethods.Add(Contact(ContactMethodKind.Email, "a@example.com", 0));
        primary.ContactMethods.Add(Contact(ContactMethodKind.Email, "b@example.com", 4));
        var duplicate = NewPerson();
        duplicate.ContactMethods.Add(Contact(ContactMethodKind.Email, "z@example.com", 7));
        duplicate.ContactMethods.Add(Contact(ContactMethodKind.Email, "y@example.com", 2));

        var moved = PersonMergeRules.Combine(primary, duplicate, null).ContactMethodSteps
            .Where(s => s.Action == ContactMethodMergeAction.MoveFromDuplicate)
            .ToList();

        moved.Select(s => s.Row.Value).Should().Equal("y@example.com", "z@example.com");
        moved.Select(s => s.SortOrder).Should().Equal(5, 6);
    }

    [Fact]
    public void Moved_rows_start_at_zero_when_the_primary_has_none()
    {
        var duplicate = NewPerson();
        duplicate.ContactMethods.Add(Contact(ContactMethodKind.Email, "a@example.com", 3));

        PersonMergeRules.Combine(NewPerson(), duplicate, null).ContactMethodSteps.Single().SortOrder.Should().Be(0);
    }

    [Fact]
    public void Repeats_within_the_duplicate_are_dropped_too()
    {
        var duplicate = NewPerson();
        duplicate.ContactMethods.Add(Contact(ContactMethodKind.Email, "a@example.com", 0));
        duplicate.ContactMethods.Add(Contact(ContactMethodKind.Email, "A@example.com", 1, "Work"));

        var merged = PersonMergeRules.Combine(NewPerson(), duplicate, null);

        merged.ContactMethodSteps.Select(s => s.Action).Should().Equal(
            ContactMethodMergeAction.MoveFromDuplicate,
            ContactMethodMergeAction.DropDuplicate);
        merged.ContactMethodSteps[0].LabelToFill.Should().BeNull("only a kept primary row takes a label");
    }

    // ---- Tags ------------------------------------------------------------------------------

    [Fact]
    public void Tags_are_unioned_by_id_primary_first()
    {
        var chess = new Tag { OwnerId = "owner-1", Name = "Chess" };
        var climbing = new Tag { OwnerId = "owner-1", Name = "Climbing" };
        var sailing = new Tag { OwnerId = "owner-1", Name = "Sailing" };
        var primary = NewPerson();
        primary.Tags.Add(climbing);
        primary.Tags.Add(chess);
        var duplicate = NewPerson();
        duplicate.Tags.Add(chess);
        duplicate.Tags.Add(sailing);

        var merged = PersonMergeRules.Combine(primary, duplicate, null);

        merged.Tags.Select(t => t.Name).Should().Equal("Climbing", "Chess", "Sailing");
        merged.TagIds.Should().BeEquivalentTo([climbing.Id, chess.Id, sailing.Id]);
        merged.NewTagNames.Should().BeNull();
    }

    // ---- As profile input ------------------------------------------------------------------

    [Fact]
    public void MergedProfile_as_input_counts_every_kept_contact_method_and_tag()
    {
        var today = new DateOnly(2026, 10, 6);
        var primary = NewPerson();
        var duplicate = NewPerson();
        for (var i = 0; i < 10; i++)
        {
            primary.ContactMethods.Add(Contact(ContactMethodKind.Email, $"p{i}@example.com", i));
            primary.Tags.Add(new Tag { OwnerId = "owner-1", Name = $"p{i}" });
        }

        for (var i = 0; i < 10; i++)
        {
            duplicate.ContactMethods.Add(Contact(ContactMethodKind.Email, $"d{i}@example.com", i));
            duplicate.Tags.Add(new Tag { OwnerId = "owner-1", Name = $"d{i}" });
        }

        PersonProfileRules.Validate(PersonMergeRules.Combine(primary, duplicate, null), today)
            .Should().BeEmpty("20 and 20 are exactly at the limits");

        duplicate.ContactMethods.Add(Contact(ContactMethodKind.Email, "extra@example.com", 10));
        duplicate.Tags.Add(new Tag { OwnerId = "owner-1", Name = "extra" });

        PersonProfileRules.Validate(PersonMergeRules.Combine(primary, duplicate, null), today)
            .Should().BeEquivalentTo([PersonValidationError.TooManyContactMethods, PersonValidationError.TooManyTags]);
    }

    [Fact]
    public void Repeated_contact_methods_do_not_count_towards_the_limit()
    {
        var primary = NewPerson();
        var duplicate = NewPerson();
        for (var i = 0; i < 15; i++)
        {
            primary.ContactMethods.Add(Contact(ContactMethodKind.Email, $"p{i}@example.com", i));
            duplicate.ContactMethods.Add(Contact(ContactMethodKind.Email, $"p{i}@example.com", i));
        }

        PersonProfileRules.Validate(PersonMergeRules.Combine(primary, duplicate, null), new DateOnly(2026, 10, 6))
            .Should().BeEmpty();
    }

    // ---- Choice validation -----------------------------------------------------------------

    [Fact]
    public void ValidateChoices_rejects_undefined_values_and_Both_for_non_text_fields()
    {
        PersonMergeRules.ValidateChoices(null);
        PersonMergeRules.ValidateChoices(new Dictionary<MergeField, MergeFieldChoice>());
        PersonMergeRules.ValidateChoices(Choose(MergeField.Details, MergeFieldChoice.Both));

        Action undefinedChoice = () => PersonMergeRules.ValidateChoices(Choose(MergeField.Name, (MergeFieldChoice)99));
        Action undefinedField = () => PersonMergeRules.ValidateChoices(Choose((MergeField)99, MergeFieldChoice.Primary));
        undefinedChoice.Should().Throw<ArgumentException>();
        undefinedField.Should().Throw<ArgumentException>();

        foreach (var field in Enum.GetValues<MergeField>().Where(f => f is not (MergeField.HowWeMet or MergeField.Details)))
        {
            Action both = () => PersonMergeRules.ValidateChoices(Choose(field, MergeFieldChoice.Both));
            both.Should().Throw<ArgumentException>($"{field} is not a text field")
                .Which.Message.Should().NotContain("Ada");
        }
    }

    [Fact]
    public void Combine_rejects_malformed_choices()
    {
        Action act = () => PersonMergeRules.Combine(NewPerson(), NewPerson(), Choose(MergeField.Name, MergeFieldChoice.Both));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Choices_for_fields_that_do_not_conflict_are_ignored()
    {
        var primary = NewPerson();
        primary.Details = "Same";
        var duplicate = NewPerson();
        duplicate.Details = "Same";
        duplicate.Nickname = "Countess";

        var merged = PersonMergeRules.Combine(primary, duplicate, new Dictionary<MergeField, MergeFieldChoice>
        {
            [MergeField.Details] = MergeFieldChoice.Both,
            [MergeField.Nickname] = MergeFieldChoice.Primary,
            [MergeField.Name] = MergeFieldChoice.Duplicate,
        });

        merged.Details.Should().Be("Same", "identical texts are not joined");
        merged.Nickname.Should().Be("Countess", "only the duplicate has one, so it is not a conflict");
        merged.FirstName.Should().Be("Ada");
    }

    [Fact]
    public void Combine_changes_neither_person()
    {
        var primary = NewPerson();
        primary.ContactMethods.Add(Contact(ContactMethodKind.Email, "a@example.com", 0));
        var duplicate = NewPerson();
        duplicate.ContactMethods.Add(Contact(ContactMethodKind.Email, "b@example.com", 0));
        duplicate.Tags.Add(new Tag { OwnerId = "owner-1", Name = "Chess" });

        PersonMergeRules.Combine(primary, duplicate, null);

        primary.ContactMethods.Should().ContainSingle();
        primary.Tags.Should().BeEmpty();
        duplicate.ContactMethods.Should().ContainSingle();
        duplicate.Tags.Should().ContainSingle();
    }
}
