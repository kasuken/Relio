namespace Relio.Application.Timeline;

/// <summary>Reads a person's private timeline from the current user's data only.</summary>
public interface IPersonTimelineService
{
    /// <summary>
    /// Reads a bounded page in descending calendar-date, audit-timestamp and stable-id order.
    /// Interactions and notes are fetched as separate, indexed keyset streams and merged in memory.
    /// A missing or foreign person returns <see langword="null"/>.
    /// </summary>
    /// <param name="personId">The person whose timeline is requested.</param>
    /// <param name="filter">The entry type to include.</param>
    /// <param name="continuation">The previous page's continuation, if any.</param>
    /// <param name="pageSize">Requested page size; clamped to 1 through 100.</param>
    /// <param name="cancellationToken">Cancels the database reads.</param>
    Task<PersonTimelinePage?> GetPageAsync(
        Guid personId,
        TimelineFilter filter = TimelineFilter.All,
        TimelineContinuation? continuation = null,
        int pageSize = 50,
        CancellationToken cancellationToken = default);
}
