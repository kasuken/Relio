namespace Relio.Application.Metrics;

/// <summary>
/// Narrow trusted background-only surface for expiring product activity contributions.
/// Do not expose it to pages or HTTP endpoints.
/// </summary>
/// <remarks>
/// When metrics are disabled, the implementation removes every remaining contribution; when
/// enabled, it removes only contributions whose fixed 90-day expiry has passed. It may return a
/// deletion count but never returns owner ids or entity data.
/// </remarks>
public interface IProductMetricsRetentionRunner
{
    /// <summary>Purges expired contributions, or all contributions when collection is disabled.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of deleted contribution rows.</returns>
    Task<long> RunAsync(CancellationToken cancellationToken = default);
}
