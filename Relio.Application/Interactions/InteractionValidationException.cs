namespace Relio.Application.Interactions;

/// <summary>
/// Thrown when an interaction request breaks one or more input rules. Its message contains only
/// validation codes, never the submitted description or participant names.
/// </summary>
public sealed class InteractionValidationException : Exception
{
    /// <summary>Creates an exception with the supplied validation codes.</summary>
    public InteractionValidationException(IReadOnlyList<InteractionValidationError> errors)
        : base($"The interaction could not be saved: {string.Join(", ", errors)}.")
    {
        Errors = errors;
    }

    /// <summary>The input rules the request broke.</summary>
    public IReadOnlyList<InteractionValidationError> Errors { get; }
}
