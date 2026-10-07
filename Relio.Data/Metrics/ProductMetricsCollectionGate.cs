namespace Relio.Data.Metrics;

/// <summary>
/// Asynchronous process-local gate shared by product activity writers and retention cleanup.
/// </summary>
/// <remarks>
/// Register one instance as a singleton. A disabling purge then waits for in-flight local writes,
/// and writers re-check the option after acquiring the same gate. This does not coordinate other
/// application processes.
/// </remarks>
public sealed class ProductMetricsCollectionGate : IProductMetricsCollectionGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <inheritdoc />
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new GateLease(_semaphore);
    }

    private sealed class GateLease(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? _semaphore = semaphore;

        public void Dispose() =>
            Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
