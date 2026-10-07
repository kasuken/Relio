namespace Relio.Application.Notes;

/// <summary>The input for creating a note about a person.</summary>
/// <param name="PersonId">The person the note belongs to.</param>
/// <param name="Text">The note text; it is trimmed before it is stored.</param>
/// <param name="IsPinned">Whether the note should be shown near the top of the person's profile.</param>
public sealed record CreateNoteRequest(Guid PersonId, string Text, bool IsPinned = false);
