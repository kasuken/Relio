namespace Relio.Data.Metrics;

/// <summary>
/// Process-wide asynchronous coordination for product activity writers and retention cleanup.
/// </summary>
/// <remarks>
/// A singleton implementation serializes collection and purge operations that use contexts in
/// this process. It is not a distributed lock across application instances.
/// </remarks>
public interface IProductMetricsCollectionGate
{
    /// <summary>Acquires exclusive access to product activity collection and cleanup.</summary>
    /// <param name="cancellationToken">Cancels waiting to acquire the gate.</param>
    /// <returns>A lease that releases the gate when disposed.</returns>
    Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default);
}
