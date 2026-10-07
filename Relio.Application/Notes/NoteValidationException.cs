namespace Relio.Application.Notes;

/// <summary>
/// Thrown when note text breaks a rule in <see cref="NoteRules"/>. The message contains error
/// codes only, never the submitted note text.
/// </summary>
public sealed class NoteValidationException : Exception
{
    /// <summary>Creates a validation exception containing the rules the text broke.</summary>
    /// <param name="errors">The validation codes; must not be null.</param>
    public NoteValidationException(IReadOnlyList<NoteValidationError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors.ToArray();
    }

    /// <summary>The validation codes raised for the input.</summary>
    public IReadOnlyList<NoteValidationError> Errors { get; }

    private static string BuildMessage(IReadOnlyList<NoteValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return $"The note could not be saved: {string.Join(", ", errors)}.";
    }
}
