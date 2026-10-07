using Relio.Domain;

namespace Relio.Application.Interactions;

/// <summary>Input for creating one interaction shared across its selected participants.</summary>
public sealed record CreateInteractionRequest
{
    /// <summary>The profile from whose context this interaction is being recorded.</summary>
    public Guid ProfilePersonId { get; init; }

    /// <summary>The calendar date on which the interaction happened in the user's time zone.</summary>
    public DateOnly OccurredOn { get; init; }

    /// <summary>The kind of interaction.</summary>
    public InteractionKind Kind { get; init; }

    /// <summary>The user's description of the interaction.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// The complete participant list. Every id must identify a person owned by the current user
    /// and include <see cref="ProfilePersonId"/>. That profile may be archived; other archived
    /// people cannot be newly added.
    /// </summary>
    public IReadOnlyList<Guid>? ParticipantIds { get; init; }
}
