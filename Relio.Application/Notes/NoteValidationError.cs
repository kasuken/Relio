namespace Relio.Application.Notes;

/// <summary>
/// Why a note was rejected. These codes carry no submitted content; <c>Relio.Web</c> provides the
/// user-facing wording.
/// </summary>
public enum NoteValidationError
{
    /// <summary>The note text is empty or whitespace after trimming.</summary>
    TextRequired,

    /// <summary>The trimmed note text is longer than <c>Note.TextMaxLength</c>.</summary>
    TextTooLong,
}
