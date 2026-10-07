using Relio.Domain;

namespace Relio.Application.Interactions;

/// <summary>Pure validation and normalization for interaction input.</summary>
public static class InteractionRules
{
    /// <summary>The maximum number of people that may participate in one interaction.</summary>
    public const int MaxParticipants = 20;

    /// <summary>Trims the description without retaining optional blank text.</summary>
    public static string NormalizeDescription(string? description) => description?.Trim() ?? string.Empty;

    /// <summary>
    /// Returns every input error in a stable order. The caller supplies <paramref name="today"/>
    /// in the owner's calendar so the rules never read the clock or infer a time zone.
    /// </summary>
    public static IReadOnlyList<InteractionValidationError> Validate(
        DateOnly occurredOn,
        InteractionKind kind,
        string? description,
        IReadOnlyList<Guid>? participantIds,
        DateOnly today,
        Guid? requiredParticipantId = null)
    {
        var errors = new List<InteractionValidationError>();

        if (occurredOn == default)
        {
            errors.Add(InteractionValidationError.DateRequired);
        }
        else if (occurredOn > today)
        {
            errors.Add(InteractionValidationError.DateInFuture);
        }

        if (!Enum.IsDefined(kind))
        {
            errors.Add(InteractionValidationError.KindInvalid);
        }

        var normalizedDescription = NormalizeDescription(description);
        if (normalizedDescription.Length == 0)
        {
            errors.Add(InteractionValidationError.DescriptionRequired);
        }
        else if (normalizedDescription.Length > Interaction.DescriptionMaxLength)
        {
            errors.Add(InteractionValidationError.DescriptionTooLong);
        }

        if (participantIds is null || participantIds.Count == 0)
        {
            errors.Add(InteractionValidationError.ParticipantsRequired);
        }
        else
        {
            if (participantIds.Count > MaxParticipants)
            {
                errors.Add(InteractionValidationError.TooManyParticipants);
            }

            if (participantIds.Contains(Guid.Empty))
            {
                errors.Add(InteractionValidationError.ParticipantIdInvalid);
            }

            if (participantIds.Count != participantIds.Distinct().Count())
            {
                errors.Add(InteractionValidationError.DuplicateParticipant);
            }

            if (requiredParticipantId is { } requiredId && !participantIds.Contains(requiredId))
            {
                errors.Add(InteractionValidationError.ProfileParticipantRequired);
            }
        }

        return errors;
    }
}
