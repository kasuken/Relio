using Relio.Domain;

namespace Relio.Application.Notes;

/// <summary>
/// Use cases for the current user's notes. Every method is scoped to the signed-in user; a note
/// belonging to someone else is indistinguishable from a note that does not exist.
/// </summary>
public interface INoteService
{
    /// <summary>
    /// Returns the current user's note, or <see langword="null"/> when it does not exist or belongs
    /// to someone else.
    /// </summary>
    /// <param name="noteId">The note id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>An untracked note snapshot, or <see langword="null"/>.</returns>
    Task<Note?> GetAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the current user's pinned notes for a person, newest first with a stable id tie-break.
    /// Archived people are included; archiving keeps their notes intact.
    /// </summary>
    /// <param name="personId">The person whose pinned notes are requested.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Untracked pinned note snapshots; an unknown or foreign person returns an empty list.</returns>
    Task<IReadOnlyList<Note>> ListPinnedAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a note owned by the current user. The person must also belong to that user.
    /// Submitted text is trimmed and must contain between 1 and <see cref="Note.TextMaxLength"/>
    /// characters after trimming.
    /// </summary>
    /// <param name="request">The person, text and initial pin state.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The created note, with audit timestamps stamped by the data context.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="NoteValidationException">The text is empty or too long.</exception>
    /// <exception cref="Relio.Application.Ownership.ForeignEntityNotOwnedException">
    /// The person does not exist or is not owned by the current user.
    /// </exception>
    Task<Note> CreateAsync(CreateNoteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the text and pin state on the current user's note. Returns
    /// <see langword="false"/> when it does not exist or belongs to someone else.
    /// </summary>
    /// <param name="noteId">The note id.</param>
    /// <param name="request">The replacement text and pin state.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when the note was found and updated; otherwise false.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="NoteValidationException">The text is empty or too long.</exception>
    Task<bool> UpdateAsync(Guid noteId, UpdateNoteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes the current user's note. Returns <see langword="false"/> when it does
    /// not exist or belongs to someone else.
    /// </summary>
    /// <param name="noteId">The note id.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when the note was deleted; otherwise false.</returns>
    Task<bool> DeleteAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets whether the current user's note is pinned. Returns <see langword="false"/> when it does
    /// not exist or belongs to someone else.
    /// </summary>
    /// <param name="noteId">The note id.</param>
    /// <param name="isPinned">The new pin state.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when the note exists for the current user; otherwise false.</returns>
    Task<bool> SetPinnedAsync(Guid noteId, bool isPinned, CancellationToken cancellationToken = default);
}
