namespace Relio.Application.Metrics;

/// <summary>
/// Records the current signed-in user's minimal product activity contribution, when an operator
/// has explicitly enabled metrics. It accepts no owner id or activity payload.
/// </summary>
/// <remarks>
/// This is an internal collection surface, not an event log. Implementations must resolve the
/// owner through <c>ICurrentUser</c>, refuse disabled or missing accounts, and do no collection
/// query or write while metrics are disabled.
/// </remarks>
public interface IProductActivityService
{
    /// <summary>Records activity for the current user, at most once per UTC calendar date.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task RecordAsync(CancellationToken cancellationToken = default);
}
