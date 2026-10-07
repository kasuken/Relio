namespace Relio.Application.Timeline;

/// <summary>One bounded page from a person's mixed timeline.</summary>
/// <param name="Items">The entries in newest-first order.</param>
/// <param name="PageSize">The bounded number of rows requested.</param>
/// <param name="HasMore">Whether at least one more entry exists.</param>
/// <param name="Continuation">The keyset position for the next page, or null at the end.</param>
public sealed record PersonTimelinePage(
    IReadOnlyList<PersonTimelineEntry> Items,
    int PageSize,
    bool HasMore,
    TimelineContinuation? Continuation);
