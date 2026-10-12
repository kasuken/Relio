using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Relio.Data.Reminders;

namespace Relio.Data.IntegrationTests.Infrastructure;

/// <summary>
/// Fails the runner's second delivery save (the one that records the outcome of the sends), once,
/// the way a dropped connection or a crash after the emails left would. The claim save is untouched.
/// </summary>
internal sealed class FailReminderFinalizeInterceptor : SaveChangesInterceptor
{
    private int _fired;

    /// <summary>True once the failure was injected.</summary>
    public bool Fired => Volatile.Read(ref _fired) == 1;

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var isFinalize = eventData.Context is { } context
            && context.ChangeTracker.Entries<ReminderDelivery>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);

        if (isFinalize && Interlocked.Exchange(ref _fired, 1) == 0)
        {
            throw new InvalidOperationException("simulated failure");
        }

        return ValueTask.FromResult(result);
    }
}
