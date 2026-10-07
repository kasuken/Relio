using System.Diagnostics;
using Relio.Application.People;
using Relio.Application.People.Import;
using Relio.Domain;

namespace Relio.Application.Tests.People.Import;

public sealed class ImportCandidateBuilderTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static ImportPersonDraft Draft(
        int row = 1,
        string? first = "Ann",
        string? last = "Example",
        string? nickname = null,
        ImportBirthdayDraft? birthday = null,
        bool unreadable = false,
        string? details = null,
        params ImportContactDraft[] contacts) =>
        new(row, first, last, nickname, birthday, unreadable, details, contacts);

    private static ImportPreview Build(IEnumerable<ImportPersonDraft> drafts, DateOnly? today = null, params DuplicateCandidate[] existing)
    {
        var list = drafts.ToList();
        return ImportCandidateBuilder.Build(
            new ImportReadResult(list, list.Count, false, 0),
            today ?? Today,
            PossibleDuplicateMatcher.Prepare(existing));
    }

    private static ImportCandidate Single(ImportPersonDraft draft, DateOnly? today = null, params DuplicateCandidate[] existing) =>
        Build([draft], today, existing).Candidates.Single();

    [Fact]
    public void A_name_missing_row_is_blocked_and_unselected()
    {
        var candidate = Single(Draft(first: null, last: "Example", contacts: new ImportContactDraft(ContactMethodKind.Email, null, "a@example.com")));

        candidate.CanImport.Should().BeFalse();
        candidate.Request.Should().BeNull();
        candidate.Problems.Should().Equal(ImportProblem.NameMissing);
        candidate.SelectedByDefault.Should().BeFalse();
    }

    [Fact]
    public void Long_names_block_the_row()
    {
        Single(Draft(first: new string('a', 101))).Problems.Should().Equal(ImportProblem.FirstNameTooLong);
        Single(Draft(last: new string('b', 101))).Problems.Should().Equal(ImportProblem.LastNameTooLong);
        Single(Draft(first: new string('a', 101))).DisplayName.Length.Should().BeLessThanOrEqualTo(80);
    }

    [Fact]
    public void A_clean_row_is_selected_by_default()
    {
        var candidate = Single(Draft(birthday: new ImportBirthdayDraft(4, 15, 1985), details: "note"));

        candidate.Problems.Should().BeEmpty();
        candidate.SelectedByDefault.Should().BeTrue();
        candidate.DisplayName.Should().Be("Ann Example");
        candidate.Request!.BirthdayDay.Should().Be(15);
    }

    [Theory]
    [InlineData("Pacific/Kiritimati", 2026, 10, 8, true)]
    [InlineData("Pacific/Pago_Pago", 2026, 10, 8, false)]
    public void A_future_birthday_is_dropped_in_the_users_time_zone(string zoneId, int year, int month, int day, bool future)
    {
        // At 2026-10-07 12:00 UTC it is already 8 October on Kiritimati (UTC+14) and still 7 October on Pago Pago.
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc), zone));

        var candidate = Single(Draft(birthday: new ImportBirthdayDraft(month, day, year)), today);

        if (future)
        {
            candidate.Problems.Should().BeEmpty();
            candidate.Request!.BirthdayYear.Should().Be(2026);
        }
        else
        {
            candidate.Problems.Should().Equal(ImportProblem.BirthdayInTheFuture);
            candidate.Request!.BirthdayDay.Should().BeNull();
            candidate.Request.BirthdayYear.Should().BeNull();
        }
    }

    [Fact]
    public void An_unreadable_birthday_is_dropped_with_a_warning()
    {
        var candidate = Single(Draft(unreadable: true));

        candidate.Problems.Should().Equal(ImportProblem.BirthdayUnreadable);
        candidate.CanImport.Should().BeTrue();
        candidate.Request!.BirthdayMonth.Should().BeNull();
    }

    [Fact]
    public void An_impossible_birthday_is_dropped()
    {
        var candidate = Single(Draft(birthday: new ImportBirthdayDraft(2, 30, null)));

        candidate.Problems.Should().Equal(ImportProblem.BirthdayNotARealDate);
        candidate.Request!.BirthdayMonth.Should().BeNull();
    }

    [Fact]
    public void Keeps_only_the_first_20_contact_methods()
    {
        var contacts = Enumerable.Range(0, 25).Select(i => new ImportContactDraft(ContactMethodKind.Email, null, $"p{i}@example.com")).ToArray();

        var candidate = Single(Draft(contacts: contacts));

        candidate.Problems.Should().Equal(ImportProblem.TooManyContactMethods);
        candidate.Request!.ContactMethods.Should().HaveCount(20);
        candidate.Request.ContactMethods[19].Value.Should().Be("p19@example.com");
    }

    [Fact]
    public void Invalid_email_and_phone_are_kept_as_other_with_a_label()
    {
        var candidate = Single(Draft(contacts:
        [
            new ImportContactDraft(ContactMethodKind.Email, "Work", "not-an-email"),
            new ImportContactDraft(ContactMethodKind.Phone, null, "call me"),
            new ImportContactDraft(ContactMethodKind.Phone, null, "+44 7700 900111 ext 5"),
            new ImportContactDraft(ContactMethodKind.Email, new string('x', 48), "also bad"),
        ]));

        candidate.Problems.Should().BeEquivalentTo([ImportProblem.EmailKeptAsOther, ImportProblem.PhoneKeptAsOther]);
        candidate.Request!.ContactMethods.Should().Equal(
            new ContactMethodInput(null, ContactMethodKind.Other, "Email · Work", "not-an-email"),
            new ContactMethodInput(null, ContactMethodKind.Other, "Phone", "call me"),
            new ContactMethodInput(null, ContactMethodKind.Other, "Phone", "+44 7700 900111 ext 5"),
            new ContactMethodInput(null, ContactMethodKind.Other, "Email", "also bad"));
    }

    [Fact]
    public void Overlong_details_nickname_and_contact_values_are_dropped()
    {
        var candidate = Single(Draft(
            nickname: new string('n', 101),
            details: new string('d', 4001),
            contacts: [new ImportContactDraft(ContactMethodKind.Address, null, new string('a', 301)), new ImportContactDraft(ContactMethodKind.Email, null, "ok@example.com")]));

        candidate.Problems.Should().BeEquivalentTo([ImportProblem.NicknameTooLong, ImportProblem.DetailsTooLong, ImportProblem.ContactValueTooLong]);
        candidate.Request!.Nickname.Should().BeNull();
        candidate.Request.Details.Should().BeNull();
        candidate.Request.ContactMethods.Should().ContainSingle().Which.Value.Should().Be("ok@example.com");
    }

    [Fact]
    public void A_label_that_is_too_long_is_dropped()
    {
        var candidate = Single(Draft(contacts: [new ImportContactDraft(ContactMethodKind.Email, new string('l', 51), "ok@example.com")]));

        candidate.Request!.ContactMethods.Single().Label.Should().BeNull();
    }

    [Fact]
    public void Blank_values_are_skipped_silently()
    {
        var candidate = Single(Draft(contacts: [new ImportContactDraft(ContactMethodKind.Email, null, "  ")]));

        candidate.Request!.ContactMethods.Should().BeEmpty();
        candidate.Problems.Should().BeEmpty();
    }

    [Fact]
    public void Flags_an_existing_person_and_starts_unselected()
    {
        var existing = new DuplicateCandidate(Guid.NewGuid(), "Ann", "Example", null, false, [], []);

        var candidate = Single(Draft(), null, existing);

        candidate.ExistingMatches.Should().ContainSingle().Which.Id.Should().Be(existing.Id);
        candidate.SelectedByDefault.Should().BeFalse();
        candidate.CanImport.Should().BeTrue();
    }

    [Fact]
    public void Matches_an_existing_person_by_email_or_phone()
    {
        var existing = new DuplicateCandidate(Guid.NewGuid(), "Somebody", "Else", null, false, ["ann@example.com"], ["+447700900123"]);

        Single(Draft(first: "Zed", last: "Other", contacts: new ImportContactDraft(ContactMethodKind.Email, null, "Ann@Example.com")), null, existing)
            .ExistingMatches.Should().ContainSingle();
        Single(Draft(first: "Zed", last: "Other", contacts: new ImportContactDraft(ContactMethodKind.Phone, null, "+44 7700 900123")), null, existing)
            .ExistingMatches.Should().ContainSingle();
    }

    [Fact]
    public void Existing_archived_people_count()
    {
        var archived = new DuplicateCandidate(Guid.NewGuid(), "Ann", "Example", null, true, [], []);

        Single(Draft(), null, archived).ExistingMatches.Should().ContainSingle().Which.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Flags_a_row_repeating_an_earlier_row()
    {
        var preview = Build(
        [
            Draft(row: 2, first: "Ann", last: "Example"),
            Draft(row: 3, first: "Bob", last: "Builder"),
            Draft(row: 7, first: "ann", last: "EXAMPLE"),
            Draft(row: 9, first: "Cleo", last: "Other", contacts: new ImportContactDraft(ContactMethodKind.Email, null, "x@example.com")),
            Draft(row: 11, first: "Dee", last: "Else", contacts: new ImportContactDraft(ContactMethodKind.Email, null, "X@example.com")),
        ]);

        preview.Candidates.Select(c => c.SameAsRowNumber).Should().Equal(null, null, 2, null, 9);
        preview.Candidates[2].SelectedByDefault.Should().BeFalse();
        preview.Candidates[0].SelectedByDefault.Should().BeTrue();
        preview.PossibleDuplicateCount.Should().Be(2);
    }

    [Fact]
    public void Blocked_rows_are_not_compared_with_anything()
    {
        var preview = Build([Draft(row: 1, first: null, last: "Example"), Draft(row: 2, first: "Ann", last: "Example")]);

        preview.Candidates[1].SameAsRowNumber.Should().BeNull();
        preview.BlockedCount.Should().Be(1);
        preview.ImportableCount.Should().Be(1);
    }

    [Fact]
    public void Every_importable_request_passes_PersonProfileRules_and_ContactMethodRules()
    {
        foreach (var name in new[] { "ios-17.vcf", "android-21-qp.vcf", "google-30.vcf", "vcard-40.vcf", "edge-cases.vcf" })
        {
            AssertClean(ImportFixtures.VCard(name));
        }

        foreach (var name in new[] { "google-contacts.csv", "google-contacts-legacy.csv", "outlook.csv", "generic.tsv" })
        {
            AssertClean(ImportFixtures.MappedCsv(name).Read);
        }

        static void AssertClean(ImportReadResult read)
        {
            var preview = ImportCandidateBuilder.Build(read, Today, PossibleDuplicateMatcher.Prepare([]));
            preview.Candidates.Should().HaveCount(read.People.Count);
            foreach (var candidate in preview.Candidates.Where(c => c.CanImport))
            {
                PersonProfileRules.Validate(candidate.Request!, Today).Should().BeEmpty();
                PersonProfileRules.ValidateContactMethods(candidate.Request!).Should().BeEmpty();
                candidate.Request!.ContactMethods.Should().OnlyContain(contact => contact.Id == null);
            }
        }
    }

    [Fact]
    public void The_edge_cases_fixture_gives_the_expected_problems()
    {
        var preview = ImportCandidateBuilder.Build(ImportFixtures.VCard("edge-cases.vcf"), Today, PossibleDuplicateMatcher.Prepare([]));
        ImportCandidate Of(string first) => preview.Candidates.Single(c => c.DisplayName.StartsWith(first, StringComparison.Ordinal));

        Of("Emails").Problems.Should().Equal(ImportProblem.TooManyContactMethods);
        Of(new string('A', 79)).Problems.Should().Equal(ImportProblem.FirstNameTooLong);
        Of("Notes").Problems.Should().Equal(ImportProblem.DetailsTooLong);
        Of("Mail").Problems.Should().BeEquivalentTo([ImportProblem.EmailKeptAsOther, ImportProblem.PhoneKeptAsOther]);
        Of("Lena").Problems.Should().BeEmpty();
    }

    [Fact]
    public void Two_thousand_rows_build_in_reasonable_time()
    {
        // Names that are unrelated to each other (no shared prefixes), in 40 families: the same-last-name
        // comparison, the most expensive one, runs for about 50 pairs per row.
        var random = new Random(29);
        string Word() => new(Enumerable.Range(0, 9).Select(_ => (char)('a' + random.Next(26))).ToArray());
        var existing = Enumerable.Range(0, 2_000)
            .Select(i => new DuplicateCandidate(Guid.NewGuid(), Word(), $"Person{i % 40}", null, false, [$"e{i}@example.com"], [$"+4477009{i:D5}"]))
            .ToList();
        var drafts = Enumerable.Range(0, ImportLimits.MaxPeople)
            .Select(i => Draft(
                row: i + 2,
                first: Word(),
                last: $"Family{i % 40}",
                contacts: [new ImportContactDraft(ContactMethodKind.Email, null, $"c{i}@example.com"), new ImportContactDraft(ContactMethodKind.Phone, null, $"+44 7800 {i:D6}")]))
            .ToList();
        var prepared = PossibleDuplicateMatcher.Prepare(existing);

        var timer = Stopwatch.StartNew();
        var preview = ImportCandidateBuilder.Build(new ImportReadResult(drafts, drafts.Count, false, 0), Today, prepared);
        timer.Stop();

        preview.Candidates.Should().HaveCount(2_000);
        // About a second on an idle machine in Release (two to three times that unoptimized). The bound
        // is wall-clock and the test projects run in parallel with browsers on CI and on a laptop, where
        // it was measured at 9 s under load, so it is a guard against an algorithmic or allocation
        // regression (the enumerator-per-pair version took over a minute), not a benchmark.
        timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }
}

public sealed class ImportPersonRequestTests
{
    [Fact]
    public void Carries_no_foreign_ids()
    {
        IPersonProfileInput request = new ImportPersonRequest { FirstName = "Ann" };

        request.RelationshipTypeId.Should().BeNull();
        request.TagIds.Should().BeNull();
        request.NewTagNames.Should().BeNull();
        request.HowWeMet.Should().BeNull();
        request.ContactMethods.Should().BeEmpty();
    }
}

public sealed class PeopleImportValidationExceptionTests
{
    [Fact]
    public void Message_lists_indexes_and_codes_never_values()
    {
        var exception = new PeopleImportValidationException(
        [
            new ImportRowError(3, [PersonValidationError.BirthdayInTheFuture], []),
            new ImportRowError(7, [], [new ContactMethodProblem(0, ContactMethodValidationError.EmailInvalid)]),
        ]);

        exception.Message.Should().Contain("[3].BirthdayInTheFuture").And.Contain("[7].ContactMethods[0].EmailInvalid");
        exception.Rows.Should().HaveCount(2);
    }

    [Fact]
    public void File_exceptions_carry_only_the_code()
    {
        new ImportFileException(ImportFileProblem.TooLarge).Message.Should().Be("TooLarge");
    }
}
