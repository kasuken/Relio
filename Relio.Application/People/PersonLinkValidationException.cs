namespace Relio.Application.People;

/// <summary>
/// Thrown when a request to link two people fails validation. The message lists the codes only,
/// never ids or names.
/// </summary>
public sealed class PersonLinkValidationException : Exception
{
    /// <summary>Creates a new instance carrying the validation error codes.</summary>
    public PersonLinkValidationException(IReadOnlyList<PersonLinkValidationError> errors)
        : base($"Person link validation failed with errors: {string.Join(", ", errors ?? [])}")
    {
        Errors = errors ?? [];
    }

    /// <summary>The validation errors identified.</summary>
    public IReadOnlyList<PersonLinkValidationError> Errors { get; }
}
