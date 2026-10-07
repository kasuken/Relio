namespace Relio.Application.Timeline;

/// <summary>
/// Per-stream keyset positions for loading the next mixed timeline page. It contains only opaque
/// ids and ordering values, never user-written content or display names.
/// </summary>
public sealed record TimelineContinuation(
    InteractionTimelineCursor? Interaction,
    NoteTimelineCursor? Note);

/// <summary>The last interaction consumed by a timeline page.</summary>
public sealed record InteractionTimelineCursor(DateOnly OccurredOn, DateTime CreatedAtUtc, Guid Id);

/// <summary>The last note consumed by a timeline page.</summary>
public sealed record NoteTimelineCursor(DateTime CreatedAtUtc, Guid Id);
