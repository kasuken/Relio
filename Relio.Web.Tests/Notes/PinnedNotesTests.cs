using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Relio.Domain;
using Relio.Web.Components.Notes;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Notes;

public class PinnedNotesTests
{
    private static BunitContext CreateContext(
        FakeNoteService notes,
        out IRenderedComponent<MudSnackbarProvider> snackbars)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<Relio.Application.Notes.INoteService>(notes);
        context.Render<MudPopoverProvider>();
        snackbars = context.Render<MudSnackbarProvider>();
        return context;
    }

    [Fact]
    public async Task Displays_pinned_notes_in_service_order_as_encoded_text()
    {
        var personId = Guid.NewGuid();
        var newer = new Note
        {
            PersonId = personId,
            Text = "<script>alert('private')</script>\nA second line.",
            IsPinned = true,
            CreatedAtUtc = new DateTime(2026, 10, 7, 10, 0, 0, DateTimeKind.Utc),
        };
        var older = new Note
        {
            PersonId = personId,
            Text = "An older note.",
            IsPinned = true,
            CreatedAtUtc = new DateTime(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc),
        };
        var notes = new FakeNoteService();
        notes.Notes.AddRange([older, newer]);
        await using var context = CreateContext(notes, out _);

        var cut = context.Render<PinnedNotes>(parameters => parameters.Add(component => component.PersonId, personId));

        cut.FindAll("[data-testid='pinned-note-text']")
            .Select(element => element.TextContent)
            .Should().Equal(newer.Text, older.Text);
        cut.FindAll("script").Should().BeEmpty();
        cut.Markup.Should().Contain("&lt;script&gt;");
        notes.PinnedQueries.Should().ContainSingle().Which.Should().Be(personId);
    }

    [Fact]
    public async Task Unpinning_a_note_updates_the_service_refreshes_the_list_and_notifies_the_user()
    {
        var personId = Guid.NewGuid();
        var note = new Note { PersonId = personId, Text = "Pinned note.", IsPinned = true };
        var notes = new FakeNoteService();
        notes.Notes.Add(note);
        await using var context = CreateContext(notes, out var snackbars);
        var changed = false;
        var cut = context.Render<PinnedNotes>(parameters => parameters
            .Add(component => component.PersonId, personId)
            .Add(component => component.Changed, EventCallback.Factory.Create(this, () => changed = true)));

        cut.Find("[data-testid='pinned-note-unpin']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='pinned-note']").Should().BeEmpty();
            changed.Should().BeTrue();
        });
        notes.PinChanges.Should().ContainSingle().Which.Should().Be((note.Id, false));
        notes.PinnedQueries.Should().HaveCount(2);
        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Note unpinned"));
    }

    [Fact]
    public async Task Edit_action_invokes_callback_with_the_note_id()
    {
        var personId = Guid.NewGuid();
        var note = new Note { PersonId = personId, Text = "Pinned note.", IsPinned = true };
        var notes = new FakeNoteService();
        notes.Notes.Add(note);
        await using var context = CreateContext(notes, out _);
        Guid? editId = null;
        var cut = context.Render<PinnedNotes>(parameters => parameters
            .Add(component => component.PersonId, personId)
            .Add(component => component.EditRequested, (Guid id) => editId = id));

        cut.Find("[data-testid='pinned-note-edit']").Click();

        cut.WaitForAssertion(() => editId.Should().Be(note.Id));
    }

    [Fact]
    public async Task Failed_load_uses_generic_wording_and_the_public_reload_method_refreshes()
    {
        var personId = Guid.NewGuid();
        var notes = new FakeNoteService
        {
            ThrowOnNextList = new InvalidOperationException("Sensitive content must not be shown."),
        };
        var note = new Note { PersonId = personId, Text = "Pinned note.", IsPinned = true };
        notes.Notes.Add(note);
        await using var context = CreateContext(notes, out _);
        var cut = context.Render<PinnedNotes>(parameters => parameters.Add(component => component.PersonId, personId));

        cut.Find("[data-testid='pinned-notes-error']").TextContent
            .Should().Contain("Pinned notes couldn't be loaded. Try again.");
        cut.Markup.Should().NotContain("Sensitive content");

        await cut.Instance.ReloadAsync();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='pinned-note']").Should().ContainSingle());
        notes.PinnedQueries.Should().HaveCount(2);
    }
}
