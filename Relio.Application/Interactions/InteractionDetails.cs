using Relio.Domain;

namespace Relio.Application.Interactions;

/// <summary>A current user's interaction and the people linked to it.</summary>
/// <param name="Id">The interaction's opaque identifier.</param>
/// <param name="OccurredOn">The calendar date in the owner's time zone.</param>
/// <param name="Kind">The kind of interaction.</param>
/// <param name="Description">The private description.</param>
/// <param name="CreatedAtUtc">The audit timestamp, as a UTC instant.</param>
/// <param name="Participants">The current owner's participants, ordered by display name.</param>
public sealed record InteractionDetails(
    Guid Id,
    DateOnly OccurredOn,
    InteractionKind Kind,
    string Description,
    DateTime CreatedAtUtc,
    IReadOnlyList<InteractionParticipantDetails> Participants);

/// <summary>A participant's minimal display information for an interaction.</summary>
/// <param name="PersonId">The person's opaque identifier.</param>
/// <param name="DisplayName">The person's display name.</param>
/// <param name="IsArchived">Whether the profile is archived.</param>
public sealed record InteractionParticipantDetails(Guid PersonId, string DisplayName, bool IsArchived);

/// <summary>A person that may be added to an interaction from the current profile.</summary>
/// <param name="PersonId">The person's opaque identifier.</param>
/// <param name="DisplayName">The person's display name.</param>
/// <param name="IsArchived">Whether the profile is archived.</param>
public sealed record InteractionParticipantOption(Guid PersonId, string DisplayName, bool IsArchived);
