namespace Relio.Application.People;

/// <summary>
/// Thrown when a relationship type or tag name cannot be saved. The message names the code, never
/// the submitted name, and there is never an inner exception: the database error behind a
/// <see cref="LabelValidationError.NameTaken"/> race quotes the duplicate key (owner id and name),
/// which must not travel any further.
/// </summary>
/// <param name="error">Why the name was refused.</param>
public sealed class LabelValidationException(LabelValidationError error)
    : Exception($"The name could not be saved: {error}.")
{
    /// <summary>Why the name was refused.</summary>
    public LabelValidationError Error { get; } = error;
}
