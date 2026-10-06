namespace Relio.Data.Administration;

/// <summary>
/// A process-wide gate that serializes account registration (issue #19), registered as a
/// singleton. It makes "is this the instance's first account?" a decision one registration at a
/// time within a process - see <see cref="AccountRegistrationService"/> for the second, database
/// level check that covers more than one process.
/// </summary>
public sealed class RegistrationLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>Waits for the gate; dispose the result to release it.</summary>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
