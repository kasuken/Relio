using Relio.Domain;

namespace Relio.Application.DifficultMoments;

/// <summary>
/// Pure validation and normalization rules for difficult moments.
/// </summary>
public static class DifficultMomentRules
{
    /// <summary>Trims the description without retaining optional blank text.</summary>
    public static string NormalizeDescription(string? description) => description?.Trim() ?? string.Empty;

    /// <summary>Trims optional narrative text, returning null if empty or whitespace.</summary>
    public static string? NormalizeOptionalText(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Validates difficult moment input and returns all errors in a deterministic order.
    /// </summary>
    public static IReadOnlyList<DifficultMomentValidationError> Validate(
        DateOnly occurredOn,
        string? description,
        string? trigger,
        string? resolution,
        string? lessonsLearned,
        DifficultMomentStatus status,
        DateOnly? resolvedOn,
        Guid? recurrenceOfId,
        Guid? currentMomentId,
        DateOnly today)
    {
        var errors = new List<DifficultMomentValidationError>();

        if (occurredOn == default)
        {
            errors.Add(DifficultMomentValidationError.DateRequired);
        }
        else if (occurredOn > today)
        {
            errors.Add(DifficultMomentValidationError.DateInFuture);
        }

        var normalizedDescription = NormalizeDescription(description);
        if (normalizedDescription.Length == 0)
        {
            errors.Add(DifficultMomentValidationError.DescriptionRequired);
        }
        else if (normalizedDescription.Length > DifficultMoment.DescriptionMaxLength)
        {
            errors.Add(DifficultMomentValidationError.DescriptionTooLong);
        }

        var normalizedTrigger = NormalizeOptionalText(trigger);
        if (normalizedTrigger is not null && normalizedTrigger.Length > DifficultMoment.TriggerMaxLength)
        {
            errors.Add(DifficultMomentValidationError.TriggerTooLong);
        }

        var normalizedResolution = NormalizeOptionalText(resolution);
        if (normalizedResolution is not null && normalizedResolution.Length > DifficultMoment.ResolutionMaxLength)
        {
            errors.Add(DifficultMomentValidationError.ResolutionTooLong);
        }

        var normalizedLessons = NormalizeOptionalText(lessonsLearned);
        if (normalizedLessons is not null && normalizedLessons.Length > DifficultMoment.LessonsLearnedMaxLength)
        {
            errors.Add(DifficultMomentValidationError.LessonsLearnedTooLong);
        }

        if (!Enum.IsDefined(status))
        {
            errors.Add(DifficultMomentValidationError.StatusInvalid);
        }

        if (resolvedOn.HasValue)
        {
            if (resolvedOn.Value > today)
            {
                errors.Add(DifficultMomentValidationError.ResolvedDateInFuture);
            }

            if (occurredOn != default && resolvedOn.Value < occurredOn)
            {
                errors.Add(DifficultMomentValidationError.ResolvedDateBeforeOccurredOn);
            }
        }

        if (currentMomentId.HasValue && recurrenceOfId.HasValue && currentMomentId.Value == recurrenceOfId.Value)
        {
            errors.Add(DifficultMomentValidationError.RecurrenceSelfReference);
        }

        return errors;
    }
}
