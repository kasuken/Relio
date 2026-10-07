using Relio.Application.Notes;

namespace Relio.Web.Components.Notes;

/// <summary>Words each note validation code for the note editor.</summary>
public static class NoteFormMessages
{
    /// <summary>Returns the user-facing message for a note validation code.</summary>
    /// <param name="error">The validation code returned by the Application layer.</param>
    /// <returns>A message that can be shown next to the note field.</returns>
    public static string For(NoteValidationError error) => error switch
    {
        NoteValidationError.TextRequired => "Enter a note.",
        NoteValidationError.TextTooLong => "A note can be up to 10,000 characters.",
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Unknown note validation code."),
    };
}
