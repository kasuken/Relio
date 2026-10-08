namespace Relio.Application.DifficultMoments;

/// <summary>Validation error codes for difficult moment inputs.</summary>
public enum DifficultMomentValidationError
{
    /// <summary>The date the difficult moment occurred is required.</summary>
    DateRequired,

    /// <summary>The date cannot be in the future.</summary>
    DateInFuture,

    /// <summary>A description of the difficult moment is required.</summary>
    DescriptionRequired,

    /// <summary>The description exceeds the maximum length.</summary>
    DescriptionTooLong,

    /// <summary>The trigger explanation exceeds the maximum length.</summary>
    TriggerTooLong,

    /// <summary>The resolution description exceeds the maximum length.</summary>
    ResolutionTooLong,

    /// <summary>Lessons learned text exceeds the maximum length.</summary>
    LessonsLearnedTooLong,

    /// <summary>The status value is not valid.</summary>
    StatusInvalid,

    /// <summary>The resolved date cannot be in the future.</summary>
    ResolvedDateInFuture,

    /// <summary>The resolved date cannot be earlier than the date the moment occurred.</summary>
    ResolvedDateBeforeOccurredOn,

    /// <summary>A difficult moment cannot be a recurrence of itself.</summary>
    RecurrenceSelfReference,
}
