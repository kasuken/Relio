using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using Relio.Application.Notes;
using Relio.Application.Ownership;
using Relio.Domain;
using Relio.Web.Components.Notes;
using Relio.Web.Tests.Shared;

namespace Relio.Web.Tests.Notes;

public class NoteEditorTests
{
    private static BunitContext CreateContext(
        FakeNoteService notes,
        out IRenderedComponent<MudSnackbarProvider> snackbars,
        ILogger<NoteEditor>? logger = null)
    {
        var context = new BunitContext();
        context.UseMudBlazor();
        context.Services.AddSingleton<INoteService>(notes);
        context.Services.AddSingleton(logger ?? NullLogger<NoteEditor>.Instance);
        context.Render<MudPopoverProvider>();
        snackbars = context.Render<MudSnackbarProvider>();
        return context;
    }

    [Fact]
    public async Task Create_mode_saves_note_and_invokes_saved_callback()
    {
        var personId = Guid.NewGuid();
        var notes = new FakeNoteService();
        await using var context = CreateContext(notes, out var snackbars);
        var saved = false;
        var cut = context.Render<NoteEditor>(parameters => parameters
            .Add(component => component.PersonId, personId)
            .Add(component => component.Saved, EventCallback.Factory.Create(this, () => saved = true)));
        cut.Find("[data-testid='note-editor-text-field'] textarea").Input("A note with care.");
        cut.Find("input[type='checkbox']").Change(true);

        cut.Find("[data-testid='note-editor-save']").Click();

        cut.WaitForAssertion(() =>
        {
            notes.Created.Should().ContainSingle();
            saved.Should().BeTrue();
        });
        notes.Created.Single().Should().Be(new CreateNoteRequest(personId, "A note with care.", IsPinned: true));
        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Note saved"));
    }

    [Fact]
    public async Task Edit_mode_loads_and_updates_the_requested_note()
    {
        var personId = Guid.NewGuid();
        var note = new Note
        {
            PersonId = personId,
            Text = "Original note.",
            IsPinned = true,
        };
        var notes = new FakeNoteService();
        notes.Notes.Add(note);
        await using var context = CreateContext(notes, out _);
        var saved = false;
        var cut = context.Render<NoteEditor>(parameters => parameters
            .Add(component => component.PersonId, personId)
            .Add(component => component.NoteId, note.Id)
            .Add(component => component.Saved, EventCallback.Factory.Create(this, () => saved = true)));

        cut.Markup.Should().Contain("Original note.");
        cut.Find("[data-testid='note-editor-text-field'] textarea").Input("A revised note.");
        cut.Find("[data-testid='note-editor-save']").Click();

        cut.WaitForAssertion(() =>
        {
            notes.Updated.Should().ContainSingle();
            saved.Should().BeTrue();
        });
        notes.Updated.Single().Should().Be((note.Id, new UpdateNoteRequest("A revised note.", IsPinned: true)));
        notes.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Validation_errors_are_shown_next_to_the_text_field_without_submitted_content()
    {
        var personId = Guid.NewGuid();
        var notes = new FakeNoteService
        {
            ThrowOnNextCreate = new NoteValidationException([NoteValidationError.TextRequired]),
        };
        await using var context = CreateContext(notes, out _);
        var cut = context.Render<NoteEditor>(parameters => parameters.Add(component => component.PersonId, personId));

        cut.Find("[data-testid='note-editor-save']").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Enter a note."));
        cut.Markup.Should().NotContain("The note could not be saved");
    }

    [Fact]
    public async Task A_stale_or_wrong_person_note_shows_the_same_unavailable_panel()
    {
        var personId = Guid.NewGuid();
        var missingNotes = new FakeNoteService();
        await using var missingContext = CreateContext(missingNotes, out _);
        var missing = missingContext.Render<NoteEditor>(parameters => parameters
            .Add(component => component.PersonId, personId)
            .Add(component => component.NoteId, Guid.NewGuid()));

        missing.Find("[data-testid='note-editor-unavailable']").TextContent
            .Should().Contain("This note is no longer available.");

        var notes = new FakeNoteService();
        notes.Notes.Add(new Note { PersonId = Guid.NewGuid(), Text = "Private note." });
        await using var wrongPersonContext = CreateContext(notes, out _);
        var wrongPerson = wrongPersonContext.Render<NoteEditor>(parameters => parameters
            .Add(component => component.PersonId, personId)
            .Add(component => component.NoteId, notes.Notes.Single().Id));

        wrongPerson.Find("[data-testid='note-editor-unavailable']").TextContent
            .Should().Be(missing.Find("[data-testid='note-editor-unavailable']").TextContent);
        wrongPerson.Markup.Should().NotContain("Private note.");
    }

    [Fact]
    public async Task Creating_for_a_person_that_disappeared_does_not_expose_exception_details()
    {
        var notes = new FakeNoteService
        {
            ThrowOnNextCreate = new ForeignEntityNotOwnedException("people"),
        };
        await using var context = CreateContext(notes, out _);
        var cut = context.Render<NoteEditor>(parameters => parameters.Add(component => component.PersonId, Guid.NewGuid()));
        cut.Find("[data-testid='note-editor-text-field'] textarea").Input("Private input.");

        cut.Find("[data-testid='note-editor-save']").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='note-editor-unavailable']").TextContent.Should()
                .Contain("This person is no longer available."));
        cut.Markup.Should().NotContain("Private input.");
        cut.Markup.Should().NotContain("ForeignEntityNotOwnedException");
    }

    [Fact]
    public async Task Unexpected_save_failures_show_generic_wording_without_exception_details()
    {
        var notes = new FakeNoteService
        {
            ThrowOnNextCreate = new InvalidOperationException("Sensitive database detail."),
        };
        var logger = new RecordingLogger<NoteEditor>();
        await using var context = CreateContext(notes, out var snackbars, logger);
        var cut = context.Render<NoteEditor>(parameters => parameters.Add(component => component.PersonId, Guid.NewGuid()));
        cut.Find("[data-testid='note-editor-text-field'] textarea").Input("A private note.");

        cut.Find("[data-testid='note-editor-save']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='note-editor-save-error']").TextContent
            .Should().Contain("The note couldn't be saved. Try again."));
        cut.Markup.Should().NotContain("Sensitive database detail.");
        var log = logger.Entries.Should().ContainSingle().Which;
        log.Level.Should().Be(LogLevel.Error);
        log.Message.Should().Be("Note save failed with exception type InvalidOperationException");
        log.Exception.Should().BeNull();
        log.Message.Should().NotContain("Sensitive database detail.");
        log.Message.Should().NotContain("A private note.");
        snackbars.Markup.Should().NotContain("Sensitive database detail.");
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
