namespace Relio.Data.Concurrency;

/// <summary>
/// Lets one database operation at a time run on a single <see cref="RelioDbContext"/>. Owned by the
/// context (<see cref="RelioDbContext.Lane"/>); every data service runs through it via
/// <see cref="DatabaseLaneProxy{TService}"/>. See "One database operation at a time" in AGENTS.md.
/// </summary>
/// <remarks>
/// <para>
/// Re-entrant within one async flow: a service that calls another data service while it runs goes
/// straight through instead of deadlocking. Calls from other flows (sibling components, a second UI
/// event) wait their turn. The "held" marker is an <see cref="AsyncLocal{T}"/> set inside an async
/// method AFTER the semaphore is acquired, so it flows into the operation but never back to the
/// caller - that is what keeps sibling calls queued.
/// </para>
/// <para>
/// Never block on <see cref="RunAsync{T}"/> synchronously (<c>.Result</c>, <c>.Wait()</c>,
/// <c>GetAwaiter().GetResult()</c>): the operation may be waiting for a continuation on the thread
/// that is blocked. There is deliberately no timeout: with re-entrancy handled, a wait lasts only as
/// long as the queued operations, and SQL command timeouts already bound each one.
/// </para>
/// </remarks>
public sealed class DatabaseLane
{
    private static readonly AsyncLocal<HeldLane?> Held = new();

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>Whether the current async flow is already running inside this lane.</summary>
    public bool IsHeldByCurrentFlow
    {
        get
        {
            for (var held = Held.Value; held is not null; held = held.Outer)
            {
                if (ReferenceEquals(held.Lane, this))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Runs <paramref name="operation"/> once no other flow is using this lane.
    /// </summary>
    /// <param name="operation">The database work. It must await everything it starts.</param>
    /// <param name="cancellationToken">Cancels the wait for the lane (not a running operation).</param>
    public async Task RunAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await RunAsync(
            async () =>
            {
                await operation();
                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="operation"/> once no other flow is using this lane, and returns its result.
    /// </summary>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="operation">The database work. It must await everything it starts.</param>
    /// <param name="cancellationToken">Cancels the wait for the lane (not a running operation).</param>
    public async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // This method MUST stay `async`, and Held must only be set AFTER the semaphore was awaited:
        // an async method's AsyncLocal changes never flow back to its caller, which is what lets the
        // caller's next sibling call find the lane busy. A non-async Task-returning variant would
        // leak the marker to the caller and let siblings bypass the gate.
        if (IsHeldByCurrentFlow)
        {
            return await operation();
        }

        await _semaphore.WaitAsync(cancellationToken);
        var outer = Held.Value;
        Held.Value = new HeldLane(this, outer);
        try
        {
            return await operation();
        }
        finally
        {
            Held.Value = outer;
            _semaphore.Release();
        }
    }

    // A chain, not a single value, so a flow that holds the lanes of two contexts (two scopes) still
    // recognises both.
    private sealed record HeldLane(DatabaseLane Lane, HeldLane? Outer);
}
