using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Relio.Data;
using Relio.Web.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Relio.Web.E2ETests.Infrastructure.AccountTestHelpers;

namespace Relio.Web.E2ETests;

/// <summary>Exercises note creation, pinning, editing, filtering and deletion in a real profile circuit.</summary>
[Collection(RelioAppCollection.Name)]
public sealed class PersonNotesTests(RelioAppFixture fixture)
{
    [Fact]
    public async Task Notes_can_be_pinned_edited_filtered_and_deleted_from_a_profile()
    {
        var page = await fixture.NewPageAsync();
        var email = NewEmail("notes");
        await RegisterAsync(page, email, StrongPassword);
        var ownerId = (await GetUserAsync(fixture.App, email))!.Id;
        var personId = await PeopleTestHelpers.CreatePersonAsync(
            fixture.App,
            ownerId,
            new Relio.Application.People.CreatePersonRequest { FirstName = "Ada", LastName = "Lovelace" });

        await RelioAppFixture.GotoAndWaitForInteractiveAsync(page, $"/people/{personId}");
        await page.GetByTestId("person-add-note").ClickAsync();
        await page.Locator("[data-testid='note-editor-text-field'] textarea")
            .FillAsync("Remember the garden conversation.");
        await page.GetByRole(AriaRole.Checkbox, new() { Name = "Pin this note near the top" }).CheckAsync();
        await page.GetByTestId("note-editor-save").ClickAsync();

        var pinnedNote = page.GetByTestId("pinned-note-text");
        await Expect(pinnedNote).ToHaveTextAsync("Remember the garden conversation.");
        await Expect(page.GetByTestId("timeline-entry-text"))
            .ToHaveTextAsync("Remember the garden conversation.");
        var pinnedNotePrecedesFacts = await page.EvaluateAsync<bool>(
            "() => !!(document.querySelector('[data-testid=\"pinned-notes\"]')"
            + ".compareDocumentPosition(document.querySelector('[data-testid=\"person-last-contacted\"]'))"
            + " & Node.DOCUMENT_POSITION_FOLLOWING)");
        pinnedNotePrecedesFacts.Should().BeTrue("pinned notes appear before the profile facts and timeline");
        await Expect(page.GetByTestId("timeline-entry-pinned")).ToContainTextAsync("Pinned");

        await page.GetByTestId("pinned-note-unpin").ClickAsync();
        await Expect(page.GetByTestId("pinned-notes")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("timeline-entry-pinned")).ToHaveCountAsync(0);

        await page.GetByTestId("timeline-edit-note").ClickAsync();
        var noteInput = page.Locator("[data-testid='note-editor-text-field'] textarea");
        await Expect(noteInput).ToHaveValueAsync("Remember the garden conversation.");
        await Expect(page.GetByRole(AriaRole.Checkbox, new() { Name = "Pin this note near the top" }))
            .Not.ToBeCheckedAsync();
        await noteInput.FillAsync("Remember the garden and observatory conversation.");
        await page.GetByTestId("note-editor-save").ClickAsync();

        await Expect(page.GetByTestId("pinned-notes")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("timeline-entry-text"))
            .ToHaveTextAsync("Remember the garden and observatory conversation.");
        using (var scope = fixture.App.CreateRealScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();
            (await dbContext.Notes.AsNoTracking()
                .SingleAsync(note => note.OwnerId == ownerId && note.PersonId == personId))
                .IsPinned.Should().BeFalse("editing an unpinned note keeps it unpinned");
        }
        await Expect(page.GetByTestId("timeline-entry-pinned")).ToHaveCountAsync(0);

        await page.GetByRole(AriaRole.Combobox, new() { Name = "Show", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "Notes", Exact = true }).ClickAsync();
        await Expect(page.GetByTestId("timeline-entry")).ToHaveCountAsync(1);
        await Expect(page.GetByTestId("timeline-entry-kind")).ToContainTextAsync("Note");

        await page.GetByTestId("timeline-delete-note").ClickAsync();
        await Expect(page.Locator(".mud-dialog")).ToContainTextAsync("This note will be permanently removed");
        await page.GetByTestId("confirm-dialog-confirm").ClickAsync();
        await Expect(page.GetByTestId("timeline-empty")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("pinned-notes")).ToHaveCountAsync(0);

        using (var scope = fixture.App.CreateRealScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<RelioDbContext>();
            (await dbContext.Notes.AsNoTracking().AnyAsync(note => note.OwnerId == ownerId && note.PersonId == personId))
                .Should().BeFalse("the note was permanently deleted");
        }

        await RelioAppFixture.ClosePageAsync(page);
    }
}
