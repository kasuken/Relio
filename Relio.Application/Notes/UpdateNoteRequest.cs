namespace Relio.Application.Notes;

/// <summary>The replacement text and pin state for an existing note.</summary>
/// <param name="Text">The note text; it is trimmed before it is stored.</param>
/// <param name="IsPinned">Whether the note should be shown near the top of the person's profile.</param>
public sealed record UpdateNoteRequest(string Text, bool IsPinned = false);
