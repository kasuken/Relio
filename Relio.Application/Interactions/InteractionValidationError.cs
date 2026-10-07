namespace Relio.Application.Interactions;

/// <summary>
/// Why an interaction request was rejected. Application exposes codes only; Relio.Web provides
/// the user-facing message beside the field that needs attention.
/// </summary>
public enum InteractionValidationError
{
    /// <summary>No interaction date was supplied.</summary>
    DateRequired,

    /// <summary>The interaction date is after today in the owner's time zone.</summary>
    DateInFuture,

    /// <summary>The request contains an undefined interaction kind.</summary>
    KindInvalid,

    /// <summary>The description is empty after trimming.</summary>
    DescriptionRequired,

    /// <summary>The description is longer than <see cref="Relio.Domain.Interaction.DescriptionMaxLength"/>.</summary>
    DescriptionTooLong,

    /// <summary>No participants were selected.</summary>
    ParticipantsRequired,

    /// <summary>The profile from which an interaction is being recorded was not included.</summary>
    ProfileParticipantRequired,

    /// <summary>More than the supported number of participants were selected.</summary>
    TooManyParticipants,

    /// <summary>A participant identifier was empty.</summary>
    ParticipantIdInvalid,

    /// <summary>The same person was selected more than once.</summary>
    DuplicateParticipant,

    /// <summary>A new archived person cannot be added as an interaction participant.</summary>
    ArchivedParticipantNotAllowed,
}
