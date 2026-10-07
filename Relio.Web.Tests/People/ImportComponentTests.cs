using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Relio.Application.People;
using Relio.Application.People.Import;
using Relio.Domain;
using Relio.Web.Components.People.Import;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.People;

/// <summary>The column matching step of a CSV import (issue #29).</summary>
public class CsvMappingStepTests
{
    private static CsvTable Table(string csv) => CsvReader.Read(Encoding.UTF8.GetBytes(csv), "a.csv");

    private static BunitContext CreateContext(out IRenderedComponent<MudPopoverProvider> popovers)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        popovers = context.Render<MudPopoverProvider>();
        return context;
    }

    private static IRenderedComponent<CsvMappingStep> Render(
        BunitContext context,
        CsvTable table,
        CsvMappingSuggestion? suggestion = null,
        Action<CsvColumnMapping>? changed = null,
        CsvColumnMapping? mapping = null)
    {
        suggestion ??= CsvMappingPresets.Suggest(table);
        return context.Render<CsvMappingStep>(parameters => parameters
            .Add(p => p.Table, table)
            .Add(p => p.Suggestion, suggestion)
            .Add(p => p.Mapping, mapping ?? suggestion.Mapping)
            .Add(p => p.MappingChanged, EventCallback.Factory.Create<CsvColumnMapping>(new object(), (Action<CsvColumnMapping>)(value => changed?.Invoke(value)))));
    }

    [Fact]
    public async Task Lists_columns_with_example_values()
    {
        await using var context = CreateContext(out _);
        var table = Table("First Name,Last Name,Notes\r\nAda,Lovelace,\"first line\nsecond line\"\r\nGrace,Hopper,\r\n");

        var cut = Render(context, table);

        cut.Find("[data-testid='import-preset']").TextContent.Should().Contain("Matched the columns");
        var columns = cut.FindAll("[data-testid='import-column']");
        columns.Should().HaveCount(3);
        columns.Select(column => column.QuerySelector("[data-testid='import-column-header']")!.TextContent)
            .Should().Equal("First Name", "Last Name", "Notes");
        columns.Select(column => column.QuerySelector("[data-testid='import-column-example']")!.TextContent)
            .Should().Equal("Ada", "Lovelace", "first line");
        columns[0].QuerySelector("input")!.GetAttribute("value").Should().Be("First name");
    }

    [Fact]
    public async Task Cuts_long_examples()
    {
        await using var context = CreateContext(out _);

        var cut = Render(context, Table("First Name\r\n" + new string('x', 100) + "\r\n"));

        cut.Find("[data-testid='import-column-example']").TextContent.Should().HaveLength(40).And.EndWith("…");
    }

    [Fact]
    public async Task Preview_is_disabled_without_a_name_column()
    {
        await using var context = CreateContext(out _);

        var cut = Render(context, Table("Vorname,Nachname\r\nAnke,Schmidt\r\n"));

        cut.Find("[data-testid='import-mapping-error']").TextContent.Should().Contain("Choose the column with the first name");
        cut.Find("[data-testid='import-mapping-error']").GetAttribute("role").Should().Be("alert");
        cut.Find("[data-testid='import-preview']").HasAttribute("disabled").Should().BeTrue();

        var withName = Render(context, Table("First Name,Last Name\r\nAnke,Schmidt\r\n"));
        withName.FindAll("[data-testid='import-mapping-error']").Should().BeEmpty();
        withName.Find("[data-testid='import-preview']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task Changing_a_field_raises_a_new_mapping()
    {
        await using var context = CreateContext(out var popovers);
        CsvColumnMapping? raised = null;
        var table = Table("Vorname,Nachname\r\nAnke,Schmidt\r\n");
        var original = CsvMappingPresets.Suggest(table);
        var cut = Render(context, table, original, mapping => raised = mapping);

        cut.FindAll("[data-testid='import-column'] .mud-input-control")[0].MouseDown();
        popovers.WaitForElements(".mud-popover-open .mud-list-item")
            .Single(item => item.TextContent.Trim() == "First name").Click();

        cut.WaitForAssertion(() => raised.Should().NotBeNull());
        raised!.Columns[0].Field.Should().Be(CsvField.FirstName);
        raised.Columns[1].Field.Should().Be(CsvField.Ignore);
        raised.Should().NotBeSameAs(original.Mapping);
        original.Mapping.Columns[0].Field.Should().Be(CsvField.Ignore, "the suggestion is immutable");
    }

    [Fact]
    public async Task Shows_the_date_order_select_only_when_ambiguous()
    {
        await using var context = CreateContext(out _);

        var ambiguous = Render(context, Table("First Name,Birthday\r\nAda,03/04/1990\r\n"));
        var decisive = Render(context, Table("First Name,Birthday\r\nAda,15/04/1990\r\n"));
        var noBirthday = Render(context, Table("First Name,Other\r\nAda,03/04/1990\r\n"));

        ambiguous.Find("[data-testid='import-date-order']").TextContent.Should().Contain("Check a birthday or two");
        decisive.FindAll("[data-testid='import-date-order']").Should().BeEmpty();
        noBirthday.FindAll("[data-testid='import-date-order']").Should().BeEmpty();
    }

    [Fact]
    public async Task Hides_label_columns_and_groups_address_blocks()
    {
        await using var context = CreateContext(out _);
        var table = Table(
            "First Name,Last Name,E-mail Address,Home Street,Home City,Home Postal Code,Business Street,Business City,Other City\r\n" +
            "Ada,Lovelace,ada@example.com,1 Example Road,London,N1 1AA,2 Example Way,Leeds,Bath\r\n");

        var cut = Render(context, table);

        var headers = cut.FindAll("[data-testid='import-column-header']").Select(e => e.TextContent).ToList();
        headers.Should().Contain("First Name").And.NotContain("Home Street").And.NotContain("Business City");
        cut.FindAll("[data-testid='import-address-block']").Should().HaveCount(3);
        cut.FindAll("[data-testid='import-address-block']")[0].TextContent.Should().Contain("Home address (3 columns)");

        var google = Render(context, Table("First Name,E-mail 1 - Label,E-mail 1 - Value\r\nAda,* Home,ada@example.com\r\n"));
        google.FindAll("[data-testid='import-column-header']").Select(e => e.TextContent)
            .Should().NotContain("E-mail 1 - Label").And.Contain("E-mail 1 - Value");
    }

    [Fact]
    public async Task Rendering_headers_and_cells_as_text_not_markup()
    {
        await using var context = CreateContext(out _);

        var cut = Render(context, Table("<b>First Name</b>,Notes\r\n<i>Ada</i>,x\r\n"));

        cut.FindAll("b, i").Should().BeEmpty();
        cut.Markup.Should().Contain("&lt;b&gt;First Name&lt;/b&gt;");
    }
}

/// <summary>The preview of an import (issue #29).</summary>
public class ImportPreviewListTests
{
    private static readonly Guid ExistingId = Guid.NewGuid();

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        return context;
    }

    private static ImportPersonDraft Draft(int row, string first, string? last = null, params ImportContactDraft[] contacts) =>
        new(row, first, last, null, null, false, null, contacts);

    private static ImportPreview PreviewOf(IEnumerable<ImportPersonDraft> drafts, params DuplicateCandidate[] existing)
    {
        var list = drafts.ToList();
        return ImportCandidateBuilder.Build(
            new ImportReadResult(list, list.Count, false, 0),
            new DateOnly(2026, 10, 7),
            PossibleDuplicateMatcher.Prepare(existing));
    }

    private static IRenderedComponent<ImportPreviewList> Render(BunitContext context, ImportPreview preview, ImportSelection? selection = null, bool busy = false) =>
        context.Render<ImportPreviewList>(parameters => parameters
            .Add(p => p.Preview, preview)
            .Add(p => p.Selection, selection ?? new ImportSelection(preview))
            .Add(p => p.Busy, busy));

    [Fact]
    public async Task Shows_rows_with_names_details_and_problems()
    {
        await using var context = CreateContext();
        var preview = PreviewOf(
        [
            new ImportPersonDraft(2, "Ada", "Lovelace", null, new ImportBirthdayDraft(12, 10, 1815), true, null,
                [new ImportContactDraft(ContactMethodKind.Email, null, "ada@example.com")]),
        ]);

        var cut = Render(context, preview);

        var row = cut.Find("[data-testid='import-row']");
        row.TextContent.Should().Contain("Ada Lovelace");
        row.QuerySelector("[data-testid='import-row-details']")!.TextContent.Should().Contain("ada@example.com");
        row.QuerySelector("[data-testid='import-row-problem']")!.TextContent.Should().Contain("birthday couldn't be read");
        cut.Find("[data-testid='import-summary']").GetAttribute("role").Should().Be("status");
        cut.Find("[data-testid='import-summary']").TextContent.Should().Contain("1 person found. 1 selected.");
        cut.FindAll("[data-testid='import-pagination']").Should().BeEmpty();
    }

    [Fact]
    public async Task Blocked_rows_have_a_disabled_checkbox_and_a_reason()
    {
        await using var context = CreateContext();
        var preview = PreviewOf([Draft(2, " ", "Nameless", new ImportContactDraft(ContactMethodKind.Email, null, "a@example.com")), Draft(3, "Ada")]);

        var cut = Render(context, preview);

        var rows = cut.FindAll("[data-testid='import-row']");
        rows[0].QuerySelector("input[data-testid='import-row-check']")!.HasAttribute("disabled").Should().BeTrue();
        rows[0].QuerySelector("[data-testid='import-row-problem']")!.TextContent.Should().Contain("can't be imported");
        rows[1].QuerySelector("input[data-testid='import-row-check']")!.HasAttribute("disabled").Should().BeFalse();
        rows[0].QuerySelector("[data-testid='import-row-details']").Should().BeNull("a blocked row has no request to summarise");
    }

    [Fact]
    public async Task Duplicate_rows_start_unchecked_and_link_to_the_existing_profile_in_a_new_tab()
    {
        await using var context = CreateContext();
        var preview = PreviewOf(
            [Draft(2, "Ada", "Lovelace"), Draft(3, "New", "Person")],
            new DuplicateCandidate(ExistingId, "Ada", "Lovelace", null, true, [], []));

        var cut = Render(context, preview);

        var rows = cut.FindAll("[data-testid='import-row']");
        rows[0].QuerySelector("input[data-testid='import-row-check']")!.HasAttribute("checked").Should().BeFalse();
        rows[1].QuerySelector("input[data-testid='import-row-check']")!.HasAttribute("checked").Should().BeTrue();
        var link = rows[0].QuerySelector("[data-testid='import-row-duplicate-link']")!;
        link.GetAttribute("href").Should().Be($"/people/{ExistingId}");
        link.GetAttribute("target").Should().Be("_blank");
        link.GetAttribute("rel").Should().Contain("noopener");
        link.TextContent.Should().Contain("Ada Lovelace").And.Contain("(opens in a new tab)");
        rows[0].QuerySelector("[data-testid='import-row-duplicate']")!.TextContent.Should().Contain("May already be in your list").And.Contain("Same name").And.Contain("Archived");
        cut.Find("[data-testid='import-confirm']").TextContent.Trim().Should().Be("Import 1 person");
    }

    [Fact]
    public async Task Same_as_row_text_names_the_row()
    {
        await using var context = CreateContext();
        var preview = PreviewOf([Draft(4, "Ada", "Lovelace"), Draft(9, "ada", "LOVELACE")]);

        var cut = Render(context, preview);

        cut.FindAll("[data-testid='import-row-same-as']").Should().ContainSingle().Which.TextContent.Should().Contain("Looks the same as row 4 in this file.");
        cut.FindAll("input[data-testid='import-row-check']")[1].HasAttribute("checked").Should().BeFalse();
    }

    [Fact]
    public async Task Pages_50_rows_and_keeps_the_selection_across_pages()
    {
        await using var context = CreateContext();
        // Unrelated names, so no row is a possible duplicate of another and every row starts selected.
        var random = new Random(5);
        var preview = PreviewOf(Enumerable.Range(0, 120).Select(i => Draft(i + 2, new string(Enumerable.Range(0, 10).Select(_ => (char)('a' + random.Next(26))).ToArray()))));
        preview.PossibleDuplicateCount.Should().Be(0);
        var selection = new ImportSelection(preview);
        var cut = Render(context, preview, selection);

        cut.FindAll("[data-testid='import-row']").Should().HaveCount(50);
        cut.Find("[data-testid='import-pagination']").Should().NotBeNull();
        cut.Find("[data-testid='import-summary']").TextContent.Should().Contain("120 people found. 120 selected.");

        // Untick a row on the first page, go to the second page and back: the choice stays.
        var firstRowId = preview.Candidates[0].RowId;
        cut.FindAll("input[data-testid='import-row-check']")[0].Change(false);
        selection.IsSelected(firstRowId).Should().BeFalse();
        cut.FindAll("[data-testid='import-pagination'] button").Single(button => button.TextContent.Trim() == "2").Click();
        cut.FindAll("[data-testid='import-row']").Should().HaveCount(50);
        cut.FindAll("[data-testid='import-row']")[0].TextContent.Should().Contain(preview.Candidates[50].DisplayName);
        cut.FindAll("[data-testid='import-pagination'] button").Single(button => button.TextContent.Trim() == "1").Click();
        cut.FindAll("input[data-testid='import-row-check']")[0].HasAttribute("checked").Should().BeFalse();
        selection.Count.Should().Be(119);
    }

    [Fact]
    public async Task The_filter_shows_only_rows_to_check()
    {
        await using var context = CreateContext();
        var preview = PreviewOf(
            [Draft(2, "Clean", "One"), Draft(3, "Dup", "Licate"), Draft(4, "  ", "Blocked")],
            new DuplicateCandidate(ExistingId, "Dup", "Licate", null, false, [], []));
        var cut = Render(context, preview);
        cut.FindAll("[data-testid='import-row']").Should().HaveCount(3);

        cut.Find("[data-testid='import-filter'] input").Change(true);

        cut.FindAll("[data-testid='import-row']").Should().HaveCount(2);
        cut.Markup.Should().NotContain("Clean One");
        cut.Find("[data-testid='import-summary']").TextContent.Should().Contain("3 people found", "the counts describe the whole file");
    }

    [Fact]
    public async Task Names_render_as_text_not_markup()
    {
        await using var context = CreateContext();
        var preview = PreviewOf([Draft(2, "<b>x</b>", "<i>y</i>", new ImportContactDraft(ContactMethodKind.Other, "<u>l</u>", "<script>alert(1)</script>"))]);

        var cut = Render(context, preview);

        cut.FindAll("b, i, u, script").Should().BeEmpty();
        cut.Find("[data-testid='import-row']").TextContent.Should().Contain("<b>x</b> <i>y</i>");
    }

    [Fact]
    public async Task Select_all_and_none_raise_no_events_but_change_the_selection()
    {
        await using var context = CreateContext();
        var preview = PreviewOf([Draft(2, "Ada", "Lovelace"), Draft(3, "Dup", "Licate")], new DuplicateCandidate(ExistingId, "Dup", "Licate", null, false, [], []));
        var selection = new ImportSelection(preview);
        var cut = Render(context, preview, selection);

        cut.Find("[data-testid='import-select-all']").Click();
        selection.Count.Should().Be(2);
        cut.Find("[data-testid='import-select-none']").Click();
        selection.Count.Should().Be(0);
        cut.Find("[data-testid='import-confirm']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task Busy_disables_every_control()
    {
        await using var context = CreateContext();
        var preview = PreviewOf([Draft(2, "Ada", "Lovelace")]);

        var cut = Render(context, preview, busy: true);

        cut.Find("[data-testid='import-confirm']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='import-cancel']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("input[data-testid='import-row-check']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='import-importing']").GetAttribute("role").Should().Be("status");
    }

    [Fact]
    public async Task A_truncated_file_says_so()
    {
        await using var context = CreateContext();
        var preview = PreviewOf([Draft(2, "Ada", "Lovelace")]) with { Truncated = true, TotalFound = 2500 };

        var cut = Render(context, preview);

        cut.Find("[data-testid='import-truncated']").TextContent.Should().Contain("more than 2,000 people");
    }

    [Fact]
    public async Task Confirm_and_cancel_raise_their_events()
    {
        await using var context = CreateContext();
        var preview = PreviewOf([Draft(2, "Ada", "Lovelace")]);
        var imported = 0;
        var cancelled = 0;
        var cut = context.Render<ImportPreviewList>(parameters => parameters
            .Add(p => p.Preview, preview)
            .Add(p => p.Selection, new ImportSelection(preview))
            .Add(p => p.OnImport, EventCallback.Factory.Create(this, () => imported++))
            .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => cancelled++)));

        cut.Find("[data-testid='import-confirm']").Click();
        cut.Find("[data-testid='import-cancel']").Click();

        (imported, cancelled).Should().Be((1, 1));
    }
}
