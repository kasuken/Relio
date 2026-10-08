using Relio.Application.Interactions;
using Relio.Domain;

namespace Relio.Application.Timeline;

/// <summary>A single, owner-scoped timeline row for a person's profile.</summary>
/// <param name="Id">An opaque entry id; never a person's name or content.</param>
/// <param name="Kind">The entry's concrete kind.</param>
/// <param name="Date">The owner's calendar date for the entry.</param>
/// <param name="CreatedAtUtc">The audit instant used to order entries on the same calendar date.</param>
/// <param name="Text">The private user-written entry text.</param>
/// <param name="InteractionKind">The category for interactions; null for other entry kinds.</param>
/// <param name="IsPinned">Whether a note is pinned; false for other entry kinds.</param>
/// <param name="Participants">Every participant in a shared interaction, empty otherwise.</param>
public sealed record PersonTimelineEntry(
    Guid Id,
    TimelineEntryKind Kind,
    DateOnly Date,
    DateTime CreatedAtUtc,
    string Text,
    InteractionKind? InteractionKind,
    bool IsPinned,
    IReadOnlyList<InteractionParticipantDetails> Participants,
    DifficultMomentStatus? DifficultMomentStatus = null,
    string? Trigger = null,
    string? Resolution = null,
    string? LessonsLearned = null,
    Guid? RecurrenceOfId = null,
    int RecurrencesCount = 0);
