using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Relio.Data.Reminders;

namespace Relio.Data.Tests.Reminders;

/// <summary>Which of the runner's two delivery saves a fault targets.</summary>
public enum DeliverySave
{
    /// <summary>The save that inserts the claim rows (some ledger row is Added).</summary>
    Claim,

    /// <summary>The save that records the outcome (some ledger row is Modified or Deleted).</summary>
    Finalize,
}

/// <summary>When a fault fires relative to the database commit.</summary>
public enum FaultTiming
{
    /// <summary>Before the save runs: nothing is persisted.</summary>
    BeforeSave,

    /// <summary>After the commit: the data is persisted but the caller sees a failure (a crash right after commit).</summary>
    AfterCommit,
}

/// <summary>
/// Fails one delivery save of the reminder runner, once. Carries no data in its message.
/// </summary>
public sealed class ReminderDeliveryFaultInterceptor(DeliverySave target, FaultTiming timing) : SaveChangesInterceptor
{
    private int _fired;
    private bool _matchedSave;

    /// <summary>Whether the fault has fired.</summary>
    public bool Fired => Volatile.Read(ref _fired) == 1;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!Fired && Matches(eventData.Context))
        {
            if (timing == FaultTiming.BeforeSave)
            {
                Fire();
            }
            else
            {
                // Entry states are reset once the commit is done, so the match is remembered now.
                _matchedSave = true;
            }
        }

        return ValueTask.FromResult(result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (_matchedSave)
        {
            _matchedSave = false;
            Fire();
        }

        return ValueTask.FromResult(result);
    }

    private bool Matches(DbContext? context)
    {
        if (context is null)
        {
            return false;
        }

        var states = context.ChangeTracker.Entries<ReminderDelivery>().Select(e => e.State).ToList();
        return target == DeliverySave.Claim
            ? states.Contains(EntityState.Added)
            : states.Contains(EntityState.Modified) || states.Contains(EntityState.Deleted);
    }

    private void Fire()
    {
        Interlocked.Exchange(ref _fired, 1);
        throw new InvalidOperationException("simulated failure");
    }
}
