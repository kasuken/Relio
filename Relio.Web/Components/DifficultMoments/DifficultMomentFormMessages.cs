using Relio.Application.DifficultMoments;

namespace Relio.Web.Components.DifficultMoments;

/// <summary>Maps difficult moment validation codes to calm, clear UI messages.</summary>
public static class DifficultMomentFormMessages
{
    /// <summary>Returns a user-facing message for every difficult moment validation code.</summary>
    public static string Message(DifficultMomentValidationError error) => error switch
    {
        DifficultMomentValidationError.DateRequired => "Choose the date when this occurred.",
        DifficultMomentValidationError.DateInFuture => "The date can't be in the future.",
        DifficultMomentValidationError.DescriptionRequired => "Describe what happened.",
        DifficultMomentValidationError.DescriptionTooLong => "Keep the description to 10,000 characters or fewer.",
        DifficultMomentValidationError.TriggerTooLong => "Keep what set this off to 10,000 characters or fewer.",
        DifficultMomentValidationError.ResolutionTooLong => "Keep how this was resolved to 10,000 characters or fewer.",
        DifficultMomentValidationError.LessonsLearnedTooLong => "Keep lessons learned to 10,000 characters or fewer.",
        DifficultMomentValidationError.StatusInvalid => "Choose a status.",
        DifficultMomentValidationError.ResolvedDateInFuture => "The resolved date can't be in the future.",
        DifficultMomentValidationError.ResolvedDateBeforeOccurredOn => "The resolved date can't be earlier than when the moment occurred.",
        DifficultMomentValidationError.RecurrenceSelfReference => "A moment cannot be a recurrence of itself.",
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Unknown difficult moment validation code."),
    };

    /// <summary>Returns the complete list of UI messages for validation errors.</summary>
    public static IReadOnlyList<string> Messages(IEnumerable<DifficultMomentValidationError> errors) =>
        errors.Select(Message).ToArray();
}
