namespace Relio.Application.DifficultMoments;

/// <summary>
/// Thrown when a difficult moment request fails validation.
/// </summary>
public sealed class DifficultMomentValidationException : Exception
{
    /// <summary>Creates a new instance carrying the validation error codes.</summary>
    public DifficultMomentValidationException(IReadOnlyList<DifficultMomentValidationError> errors)
        : base($"Difficult moment validation failed with errors: {string.Join(", ", errors)}")
    {
        Errors = errors ?? [];
    }

    /// <summary>The validation errors identified.</summary>
    public IReadOnlyList<DifficultMomentValidationError> Errors { get; }
}
