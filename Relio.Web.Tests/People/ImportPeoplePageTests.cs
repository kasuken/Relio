using System.Text;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Application.People;
using Relio.Application.People.Import;
using Relio.Application.Security;
using Relio.Web.Components.Pages;
using Relio.Web.Components.People.Import;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.People;

/// <summary>The import page (issue #29): choose a file, match the columns of a CSV, choose who to import, confirm.</summary>
public class ImportPeoplePageTests
{
    private const string TwoCards =
        "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Lovelace;Ada;;;\r\nEMAIL:ada@example.com\r\nEND:VCARD\r\n" +
        "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Hopper;Grace;;;\r\nTEL:+44 7700 900123\r\nEND:VCARD\r\n";

    private sealed record Harness(FakePeopleImportService Import, IRenderedComponent<MudSnackbarProvider> Snackbars);

    private static BunitContext CreateContext(out Harness harness)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var import = new FakePeopleImportService();
        context.Services.AddSingleton<IPeopleImportService>(import);
        context.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ImportPeople>>(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ImportPeople>.Instance);
        harness = new Harness(import, context.Render<MudSnackbarProvider>());
        return context;
    }

    private static IRenderedComponent<ImportPeople> RenderPage(BunitContext context)
    {
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/people/import");
        return context.Render<ImportPeople>();
    }

    private static void Upload(IRenderedComponent<ImportPeople> cut, string text, string name = "contacts.vcf") =>
        Upload(cut, Encoding.UTF8.GetBytes(text), name);

    private static void Upload(IRenderedComponent<ImportPeople> cut, byte[] bytes, string name)
    {
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(bytes, name));
    }

    // ---- The first step -----------------------------------------------------------------------

    [Fact]
    public async Task Has_one_h1_a_generic_title_and_a_back_link()
    {
        await using var context = CreateContext(out _);

        var cut = RenderPage(context);

        cut.FindAll("h1").Should().ContainSingle().Which.TextContent.Should().Be("Import people");
        cut.Find("[data-testid='import-back']").GetAttribute("href").Should().Be("/people");
        cut.Markup.Should().Contain("Nothing is saved until you confirm.").And.Contain("is not kept");
        cut.Find("[data-testid='import-file'] input[type=file]").Should().NotBeNull();
        cut.Find("[data-testid='import-choose-file']").TextContent.Trim().Should().Be("Choose a file");
        cut.FindAll("[data-testid='import-file-error']").Should().BeEmpty();
    }

    [Fact]
    public async Task Choosing_a_vcard_shows_the_preview_without_importing()
    {
        await using var context = CreateContext(out var harness);
        var cut = RenderPage(context);

        Upload(cut, TwoCards);

        cut.WaitForElement("[data-testid='import-list']");
        cut.FindAll("[data-testid='import-row']").Should().HaveCount(2);
        cut.Find("[data-testid='import-summary']").TextContent.Should().Contain("2 people found. 2 selected.");
        cut.Find("[data-testid='import-confirm']").TextContent.Trim().Should().Be("Import 2 people");
        harness.Import.Previews.Should().ContainSingle();
        harness.Import.Imports.Should().BeEmpty("nothing is imported until the person confirms");
    }

    [Fact]
    public async Task Choosing_a_csv_shows_the_mapping_step_with_the_preset()
    {
        await using var context = CreateContext(out var harness);
        var cut = RenderPage(context);

        Upload(cut, "First Name,Last Name,E-mail 1 - Value\r\nAda,Lovelace,ada@example.com\r\n", "contacts.csv");

        cut.WaitForElement("[data-testid='import-map']");
        cut.Find("[data-testid='import-preset']").TextContent.Should().Contain("Google Contacts export");
        cut.FindAll("[data-testid='import-column']").Should().HaveCount(3);
        harness.Import.Previews.Should().BeEmpty("the preview waits for the mapping");

        cut.Find("[data-testid='import-preview']").Click();

        cut.WaitForElement("[data-testid='import-list']");
        cut.Find("[data-testid='import-row-details']").TextContent.Should().Contain("ada@example.com");
        harness.Import.Previews.Should().ContainSingle();
        harness.Import.Imports.Should().BeEmpty();
    }

    [Fact]
    public async Task A_file_over_1_MB_is_explained_and_nothing_is_read()
    {
        await using var context = CreateContext(out var harness);
        var cut = RenderPage(context);

        Upload(cut, new byte[ImportLimits.MaxFileBytes + 1], "big.vcf");

        cut.WaitForElement("[data-testid='import-file-error']").TextContent.Should().Contain("larger than 1 MB");
        harness.Import.Previews.Should().BeEmpty();
        cut.FindAll("[data-testid='import-list']").Should().BeEmpty();
    }

    [Fact]
    public async Task An_empty_file_or_a_non_contacts_file_is_explained()
    {
        await using var context = CreateContext(out _);
        var cut = RenderPage(context);

        Upload(cut, [], "empty.vcf");
        cut.WaitForElement("[data-testid='import-file-error']").TextContent.Should().Contain("This file is empty");

        Upload(cut, "just some words", "notes.docx");
        cut.WaitForAssertion(() => cut.Find("[data-testid='import-file-error']").TextContent.Should().Contain("doesn't look like a vCard or CSV"));
    }

    [Fact]
    public async Task A_file_without_people_is_explained()
    {
        await using var context = CreateContext(out var harness);
        var cut = RenderPage(context);

        Upload(cut, "BEGIN:VCARD\r\nVERSION:3.0\r\nEND:VCARD\r\n");

        cut.WaitForElement("[data-testid='import-file-error']").TextContent.Should().Contain("No people were found");
        harness.Import.Previews.Should().BeEmpty();
    }

    [Fact]
    public async Task A_file_name_is_never_shown_back()
    {
        await using var context = CreateContext(out _);
        var cut = RenderPage(context);

        Upload(cut, TwoCards, "ada-private-contacts.vcf");
        cut.WaitForElement("[data-testid='import-list']");

        cut.Markup.Should().NotContain("ada-private-contacts");
    }

    // ---- Confirming ---------------------------------------------------------------------------

    [Fact]
    public async Task Import_sends_only_the_selected_rows_then_shows_a_snackbar_and_opens_the_list_sorted_by_added()
    {
        await using var context = CreateContext(out var harness);
        var cut = RenderPage(context);
        Upload(cut, TwoCards);
        cut.WaitForElement("[data-testid='import-list']");

        cut.FindAll("input[data-testid='import-row-check']")[1].Change(false);
        cut.Find("[data-testid='import-confirm']").TextContent.Trim().Should().Be("Import 1 person");
        cut.Find("[data-testid='import-confirm']").Click();

        cut.WaitForAssertion(() => harness.Import.Imports.Should().ContainSingle());
        harness.Import.Imports[0].Select(request => request.FirstName).Should().Equal("Ada");
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => navigation.Uri.Should().EndWith("/people?sort=added"));
        harness.Snackbars.WaitForAssertion(() => harness.Snackbars.Markup.Should().Contain("Imported 1 person"));
    }

    [Fact]
    public async Task A_validation_failure_reloads_the_preview_and_explains_it()
    {
        await using var context = CreateContext(out var harness);
        var cut = RenderPage(context);
        Upload(cut, TwoCards);
        cut.WaitForElement("[data-testid='import-list']");
        harness.Import.ThrowOnNextImport = new PeopleImportValidationException([new ImportRowError(0, [PersonValidationError.BirthdayInTheFuture], [])]);

        cut.Find("[data-testid='import-confirm']").Click();

        harness.Snackbars.WaitForAssertion(() => harness.Snackbars.Markup.Should().Contain("Some details changed since the preview"));
        harness.Import.Previews.Should().HaveCount(2, "the preview was built again");
        cut.Find("[data-testid='import-list']").Should().NotBeNull();
        context.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/people/import");
    }

    [Fact]
    public async Task A_failed_import_says_nothing_was_saved_and_stays_on_the_preview()
    {
        await using var context = CreateContext(out var harness);
        var cut = RenderPage(context);
        Upload(cut, TwoCards);
        cut.WaitForElement("[data-testid='import-list']");
        harness.Import.ThrowOnNextImport = new InvalidOperationException("secret detail Ada Lovelace");

        cut.Find("[data-testid='import-confirm']").Click();

        harness.Snackbars.WaitForAssertion(() => harness.Snackbars.Markup.Should().Contain("Nothing was saved"));
        harness.Snackbars.Markup.Should().NotContain("secret detail");
        cut.Find("[data-testid='import-list']").Should().NotBeNull();
        context.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/people/import");
    }

    [Fact]
    public async Task An_unauthenticated_failure_is_not_swallowed()
    {
        await using var context = CreateContext(out var harness);
        var cut = RenderPage(context);
        Upload(cut, TwoCards);
        cut.WaitForElement("[data-testid='import-list']");
        harness.Import.ThrowOnNextImport = new UnauthenticatedUserException();

        var act = () =>
        {
            cut.Find("[data-testid='import-confirm']").Click();
            cut.WaitForAssertion(() => throw new InvalidOperationException("waiting"), TimeSpan.FromMilliseconds(300));
        };

        act.Should().Throw<Exception>();
        harness.Snackbars.Markup.Should().NotContain("Nothing was saved");
    }

    [Fact]
    public async Task Cancel_returns_to_the_list_without_importing()
    {
        await using var context = CreateContext(out var harness);
        var cut = RenderPage(context);
        Upload(cut, TwoCards);
        cut.WaitForElement("[data-testid='import-list']");

        cut.Find("[data-testid='import-cancel']").Click();

        context.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/people");
        harness.Import.Imports.Should().BeEmpty();
    }

    [Fact]
    public async Task The_import_button_is_disabled_with_nothing_selected()
    {
        await using var context = CreateContext(out _);
        var cut = RenderPage(context);
        Upload(cut, TwoCards);
        cut.WaitForElement("[data-testid='import-list']");

        cut.Find("[data-testid='import-select-none']").Click();

        cut.Find("[data-testid='import-confirm']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='import-summary']").TextContent.Should().Contain("0 selected");
        cut.Find("[data-testid='import-select-all']").Click();
        cut.Find("[data-testid='import-confirm']").HasAttribute("disabled").Should().BeFalse();
    }
}
