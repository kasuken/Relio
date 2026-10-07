using Relio.Domain;

namespace Relio.Application.Interactions;

/// <summary>Input for replacing an interaction's date, kind, description and full participant list.</summary>
public sealed record UpdateInteractionRequest
{
    /// <summary>The calendar date on which the interaction happened in the user's time zone.</summary>
    public DateOnly OccurredOn { get; init; }

    /// <summary>The kind of interaction.</summary>
    public InteractionKind Kind { get; init; }

    /// <summary>The user's description of the interaction.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// The complete participant list. Every id must identify a person owned by the current user.
    /// Archived people already participating may remain selected.
    /// </summary>
    public IReadOnlyList<Guid>? ParticipantIds { get; init; }
}
