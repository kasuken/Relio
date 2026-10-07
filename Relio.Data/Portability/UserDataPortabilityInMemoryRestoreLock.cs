using Microsoft.EntityFrameworkCore;

namespace Relio.Data.Portability;

internal static class UserDataPortabilityInMemoryRestoreLock
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Gate> Gates = new(StringComparer.Ordinal);

    public static async ValueTask<IAsyncDisposable> EnterAsync(
        RelioDbContext dbContext,
        string ownerId,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsInMemory())
        {
            return NoOpScope.Instance;
        }

        Gate gate;
        lock (Sync)
        {
            if (Gates.TryGetValue(ownerId, out var existing))
            {
                gate = existing;
            }
            else
            {
                gate = new Gate();
                Gates.Add(ownerId, gate);
            }

            gate.References++;
        }

        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken);
            return new Scope(ownerId, gate);
        }
        catch
        {
            RemoveReference(ownerId, gate, releaseSemaphore: false);
            throw;
        }
    }

    private static void RemoveReference(string ownerId, Gate gate, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            gate.Semaphore.Release();
        }

        lock (Sync)
        {
            gate.References--;
            if (gate.References == 0
                && Gates.TryGetValue(ownerId, out var current)
                && ReferenceEquals(current, gate))
            {
                Gates.Remove(ownerId);
                gate.Semaphore.Dispose();
            }
        }
    }

    private sealed class Gate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int References { get; set; }
    }

    private sealed class Scope(string ownerId, Gate gate) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                RemoveReference(ownerId, gate, releaseSemaphore: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpScope : IAsyncDisposable
    {
        public static NoOpScope Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
