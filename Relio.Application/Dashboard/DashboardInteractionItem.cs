using Relio.Domain;

namespace Relio.Application.Dashboard;

/// <summary>A recent interaction and its active, current-user-owned participants.</summary>
/// <param name="Id">The interaction's opaque identifier.</param>
/// <param name="OccurredOn">The calendar date on which it happened in the user's time zone.</param>
/// <param name="Kind">The kind of interaction.</param>
/// <param name="Description">The user's private description.</param>
/// <param name="Participants">The active people linked to the interaction, in display-name order.</param>
public sealed record DashboardInteractionItem(
    Guid Id,
    DateOnly OccurredOn,
    InteractionKind Kind,
    string Description,
    IReadOnlyList<DashboardInteractionParticipant> Participants);

/// <summary>The minimal active-person details shown for a dashboard interaction.</summary>
/// <param name="PersonId">The person's opaque identifier.</param>
/// <param name="DisplayName">The person's display name.</param>
public sealed record DashboardInteractionParticipant(Guid PersonId, string DisplayName);
