using Microsoft.AspNetCore.Components.Forms;
using Relio.Application.People;
using Relio.Application.People.Import;
using Relio.Domain;
using Relio.Web.Components.People.Import;

namespace Relio.Web.Tests.People;

public class ImportTextTests
{
    [Fact]
    public void Every_file_problem_has_a_message()
    {
        foreach (var problem in Enum.GetValues<ImportFileProblem>())
        {
            ImportText.FileProblem(problem).Should().NotBeNullOrWhiteSpace();
        }

        var act = () => ImportText.FileProblem((ImportFileProblem)999);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Every_import_problem_has_a_message()
    {
        foreach (var problem in Enum.GetValues<ImportProblem>())
        {
            ImportText.Problem(problem).Should().NotBeNullOrWhiteSpace().And.NotContain("!");
        }

        var act = () => ImportText.Problem((ImportProblem)999);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Every_csv_field_preset_and_date_order_has_a_name()
    {
        foreach (var field in Enum.GetValues<CsvField>())
        {
            ImportText.FieldName(field).Should().NotBeNullOrWhiteSpace();
        }

        foreach (var preset in Enum.GetValues<CsvPreset>())
        {
            ImportText.PresetName(preset).Should().NotBeNullOrWhiteSpace();
            ImportText.LooksLike(preset).Should().EndWith(".");
        }

        foreach (var order in Enum.GetValues<DateOrder>())
        {
            ImportText.DateOrderName(order).Should().NotBeNullOrWhiteSpace();
        }

        new Action[]
        {
            () => ImportText.FieldName((CsvField)999),
            () => ImportText.PresetName((CsvPreset)999),
            () => ImportText.DateOrderName((DateOrder)999),
        }.Should().AllSatisfy(act => act.Should().Throw<ArgumentOutOfRangeException>());
    }

    [Fact]
    public void Import_button_and_snackbar_pluralise()
    {
        ImportText.ImportButton(1).Should().Be("Import 1 person");
        ImportText.ImportButton(12).Should().Be("Import 12 people");
        ImportText.ImportButton(0).Should().Be("Import people");
        ImportText.Imported(1).Should().Be("Imported 1 person");
        ImportText.Imported(12).Should().Be("Imported 12 people");
        ImportText.Imported(1500).Should().Be("Imported 1,500 people");
    }

    [Fact]
    public void Summary_counts_people_selected_and_skipped_entries()
    {
        var preview = new ImportPreview([Candidate("Ada"), Candidate("Grace")], 2, false, 0);

        ImportText.Summary(preview, 1).Should().Be("2 people found. 1 selected.");
        ImportText.Summary(new ImportPreview([Candidate("Ada")], 1, false, 0), 1).Should().Be("1 person found. 1 selected.");
        ImportText.Summary(new ImportPreview([Candidate("Ada")], 1, false, 3), 1).Should().Be("1 person found. 1 selected. 3 empty entries were skipped.");
    }

    [Fact]
    public void Texts_are_the_planned_ones()
    {
        ImportText.SameAsRow(3).Should().Be("Looks the same as row 3 in this file.");
        ImportText.AddressBlockName("Home", 5).Should().Be("Home address (5 columns)");
        ImportText.AddressBlockName(null, 1).Should().Be("Address (1 column)");
        ImportText.LooksLike(CsvPreset.Outlook).Should().Be("Looks like an Outlook export.");
    }

    [Fact]
    public void Contact_summary_shows_birthday_first_email_first_phone_and_more()
    {
        var request = new ImportPersonRequest
        {
            FirstName = "Ada",
            BirthdayDay = 15,
            BirthdayMonth = 4,
            ContactMethods =
            [
                new ContactMethodInput(null, ContactMethodKind.Address, null, "1 Example Road"),
                new ContactMethodInput(null, ContactMethodKind.Email, null, "ada@example.com"),
                new ContactMethodInput(null, ContactMethodKind.Email, null, "second@example.com"),
                new ContactMethodInput(null, ContactMethodKind.Phone, null, "+44 7700 900123"),
            ],
        };

        ImportText.ContactSummary(request).Should().Be("Birthday 15 April · ada@example.com · +44 7700 900123 · 2 more");
        ImportText.ContactSummary(new ImportPersonRequest { FirstName = "Bea", BirthdayDay = 1, BirthdayMonth = 2, BirthdayYear = 1990 }).Should().Be("Birthday 1 February 1990");
        ImportText.ContactSummary(new ImportPersonRequest { FirstName = "Cleo" }).Should().BeEmpty();
    }

    internal static ImportCandidate Candidate(
        string name,
        bool canImport = true,
        IReadOnlyList<PossibleDuplicate>? existing = null,
        int? sameAs = null,
        IReadOnlyList<ImportProblem>? problems = null,
        int row = 2) =>
        new(
            row,
            Guid.NewGuid(),
            name,
            canImport ? new ImportPersonRequest { FirstName = name } : null,
            problems ?? [],
            existing ?? [],
            sameAs);
}

public class ImportSelectionTests
{
    private static ImportPreview Preview(params ImportCandidate[] candidates) => new(candidates, candidates.Length, false, 0);

    private static readonly PossibleDuplicate Match = new(Guid.NewGuid(), "Ada", "Lovelace", false, [PossibleDuplicateReason.SameName]);

    [Fact]
    public void Defaults_select_clean_rows_only()
    {
        var clean = ImportTextTests.Candidate("Clean");
        var duplicate = ImportTextTests.Candidate("Dup", existing: [Match]);
        var repeat = ImportTextTests.Candidate("Repeat", sameAs: 2);
        var blocked = ImportTextTests.Candidate("Blocked", canImport: false);

        var selection = new ImportSelection(Preview(clean, duplicate, repeat, blocked));

        selection.Count.Should().Be(1);
        selection.IsSelected(clean.RowId).Should().BeTrue();
        new[] { duplicate, repeat, blocked }.Should().OnlyContain(candidate => !selection.IsSelected(candidate.RowId));
    }

    [Fact]
    public void A_warning_row_is_still_selected_by_default()
    {
        var warned = ImportTextTests.Candidate("Warned", problems: [ImportProblem.BirthdayUnreadable]);

        new ImportSelection(Preview(warned)).IsSelected(warned.RowId).Should().BeTrue();
    }

    [Fact]
    public void Blocked_rows_can_never_be_selected()
    {
        var blocked = ImportTextTests.Candidate("Blocked", canImport: false);
        var selection = new ImportSelection(Preview(blocked));

        selection.Set(blocked.RowId, true);
        selection.SelectAll();

        selection.IsSelected(blocked.RowId).Should().BeFalse();
        selection.Count.Should().Be(0);
        selection.Set(Guid.NewGuid(), true);
        selection.Count.Should().Be(0, "an id that is not in the preview is ignored");
    }

    [Fact]
    public void Select_all_includes_duplicates_but_not_blocked()
    {
        var clean = ImportTextTests.Candidate("Clean");
        var duplicate = ImportTextTests.Candidate("Dup", existing: [Match]);
        var blocked = ImportTextTests.Candidate("Blocked", canImport: false);
        var selection = new ImportSelection(Preview(clean, duplicate, blocked));

        selection.SelectAll();

        selection.Count.Should().Be(2);
        selection.IsSelected(duplicate.RowId).Should().BeTrue();
        selection.IsSelected(blocked.RowId).Should().BeFalse();
    }

    [Fact]
    public void Select_none_clears()
    {
        var selection = new ImportSelection(Preview(ImportTextTests.Candidate("A"), ImportTextTests.Candidate("B")));

        selection.SelectNone();

        selection.Count.Should().Be(0);
        selection.SelectedRequests().Should().BeEmpty();
    }

    [Fact]
    public void A_row_can_be_toggled()
    {
        var row = ImportTextTests.Candidate("Dup", existing: [Match]);
        var selection = new ImportSelection(Preview(row));

        selection.Set(row.RowId, true);
        selection.IsSelected(row.RowId).Should().BeTrue();
        selection.Set(row.RowId, false);
        selection.IsSelected(row.RowId).Should().BeFalse();
    }

    [Fact]
    public void Selected_requests_keep_row_order()
    {
        var first = ImportTextTests.Candidate("First");
        var second = ImportTextTests.Candidate("Second");
        var third = ImportTextTests.Candidate("Third");
        var selection = new ImportSelection(Preview(first, second, third));
        selection.SelectNone();

        selection.Set(third.RowId, true);
        selection.Set(first.RowId, true);

        selection.SelectedRequests().Select(request => request.FirstName).Should().Equal("First", "Third");
    }
}

public class ImportFileReaderTests
{
    private sealed class FakeBrowserFile(long size, byte[]? content = null, bool throwOnOpen = false) : IBrowserFile
    {
        public bool Opened { get; private set; }

        public string Name => "private-name.vcf";

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public long Size => size;

        public string ContentType => "text/vcard";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            Opened = true;
            if (throwOnOpen)
            {
                throw new IOException("failure mentioning private-name.vcf");
            }

            return new MemoryStream(content ?? []);
        }
    }

    [Fact]
    public async Task Reads_the_file_into_memory()
    {
        var file = new FakeBrowserFile(3, [1, 2, 3]);

        (await ImportFileReader.ReadAsync(file, CancellationToken.None)).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task An_empty_file_is_refused_before_it_is_opened()
    {
        var file = new FakeBrowserFile(0);

        var act = () => ImportFileReader.ReadAsync(file, CancellationToken.None);

        (await act.Should().ThrowAsync<ImportFileException>()).Which.Problem.Should().Be(ImportFileProblem.Empty);
        file.Opened.Should().BeFalse();
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_before_it_is_opened()
    {
        var file = new FakeBrowserFile(ImportLimits.MaxFileBytes + 1);

        var act = () => ImportFileReader.ReadAsync(file, CancellationToken.None);

        (await act.Should().ThrowAsync<ImportFileException>()).Which.Problem.Should().Be(ImportFileProblem.TooLarge);
        file.Opened.Should().BeFalse();
    }

    [Fact]
    public async Task A_stream_failure_is_unreadable_and_never_carries_the_message()
    {
        var file = new FakeBrowserFile(10, throwOnOpen: true);

        var act = () => ImportFileReader.ReadAsync(file, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<ImportFileException>()).Which;
        thrown.Problem.Should().Be(ImportFileProblem.Unreadable);
        thrown.Message.Should().NotContain("private-name");
    }

    [Fact]
    public async Task A_cancelled_read_is_not_a_file_problem()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var file = new FakeBrowserFile(3, [1, 2, 3]);

        // A MemoryStream ignores the token, so cancel through a stream that honours it.
        var act = () => ImportFileReader.ReadAsync(new CancellingFile(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        file.Opened.Should().BeFalse();
    }

    private sealed class CancellingFile : IBrowserFile
    {
        public string Name => "x";

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public long Size => 10;

        public string ContentType => "text/plain";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException(cancellationToken);
    }
}
