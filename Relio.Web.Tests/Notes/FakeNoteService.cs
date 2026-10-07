using Relio.Application.Notes;
using Relio.Domain;

namespace Relio.Web.Tests.Notes;

internal sealed class FakeNoteService : INoteService
{
    public List<Note> Notes { get; } = [];

    public List<Guid> PinnedQueries { get; } = [];

    public List<CreateNoteRequest> Created { get; } = [];

    public List<(Guid NoteId, UpdateNoteRequest Request)> Updated { get; } = [];

    public List<(Guid NoteId, bool IsPinned)> PinChanges { get; } = [];

    public bool UpdateResult { get; set; } = true;

    public bool SetPinnedResult { get; set; } = true;

    public Exception? ThrowOnNextList { get; set; }

    public Exception? ThrowOnNextCreate { get; set; }

    public Task<Note?> GetAsync(Guid noteId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Notes.FirstOrDefault(note => note.Id == noteId));

    public Task<IReadOnlyList<Note>> ListPinnedAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        PinnedQueries.Add(personId);
        if (ThrowOnNextList is { } exception)
        {
            ThrowOnNextList = null;
            throw exception;
        }

        IReadOnlyList<Note> notes = Notes
            .Where(note => note.PersonId == personId && note.IsPinned)
            .OrderByDescending(note => note.CreatedAtUtc)
            .ThenByDescending(note => note.Id)
            .ToList();
        return Task.FromResult(notes);
    }

    public Task<Note> CreateAsync(CreateNoteRequest request, CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextCreate is { } exception)
        {
            ThrowOnNextCreate = null;
            throw exception;
        }

        Created.Add(request);
        var note = new Note
        {
            OwnerId = "test-owner",
            PersonId = request.PersonId,
            Text = request.Text.Trim(),
            IsPinned = request.IsPinned,
        };
        Notes.Add(note);
        return Task.FromResult(note);
    }

    public Task<bool> UpdateAsync(
        Guid noteId, UpdateNoteRequest request, CancellationToken cancellationToken = default)
    {
        if (!UpdateResult)
        {
            return Task.FromResult(false);
        }

        Updated.Add((noteId, request));
        var note = Notes.FirstOrDefault(candidate => candidate.Id == noteId);
        if (note is not null)
        {
            note.Text = request.Text.Trim();
            note.IsPinned = request.IsPinned;
        }

        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        Notes.RemoveAll(note => note.Id == noteId);
        return Task.FromResult(true);
    }

    public Task<bool> SetPinnedAsync(
        Guid noteId, bool isPinned, CancellationToken cancellationToken = default)
    {
        PinChanges.Add((noteId, isPinned));
        var note = Notes.FirstOrDefault(candidate => candidate.Id == noteId);
        if (note is null || !SetPinnedResult)
        {
            return Task.FromResult(false);
        }

        note.IsPinned = isPinned;
        return Task.FromResult(true);
    }
}
