using Relio.Domain;

namespace Relio.Application.Notes;

/// <summary>
/// Pure note-text normalization and validation rules. They have no database, clock or current-user
/// dependency, and measure length after trimming to match the text stored by the service.
/// </summary>
public static class NoteRules
{
    /// <summary>Trims note text; a null value becomes an empty string.</summary>
    /// <param name="text">The text to normalize.</param>
    /// <returns>The trimmed text, or an empty string when <paramref name="text"/> is null.</returns>
    public static string NormalizeText(string? text) => text?.Trim() ?? string.Empty;

    /// <summary>
    /// Checks the trimmed text and returns every rule it breaks. An empty list means it can be
    /// stored.
    /// </summary>
    /// <param name="text">The text to validate.</param>
    /// <returns>The validation codes, without including the submitted text.</returns>
    public static IReadOnlyList<NoteValidationError> Validate(string? text)
    {
        var normalized = NormalizeText(text);

        if (normalized.Length == 0)
        {
            return [NoteValidationError.TextRequired];
        }

        if (normalized.Length > Note.TextMaxLength)
        {
            return [NoteValidationError.TextTooLong];
        }

        return [];
    }
}
