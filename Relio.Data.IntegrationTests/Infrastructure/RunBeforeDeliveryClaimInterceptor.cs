using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Relio.Data.Reminders;

namespace Relio.Data.IntegrationTests.Infrastructure;

/// <summary>
/// Runs an action once, just before the reminder runner saves the delivery claims of one owner:
/// "another worker got there first". The shared runner sweeps every owner, so
/// <see cref="RunOnFirstSaveInterceptor"/> is not precise enough. The action must use other contexts.
/// </summary>
internal sealed class RunBeforeDeliveryClaimInterceptor(
    string userId,
    Func<CancellationToken, Task> action) : SaveChangesInterceptor
{
    private int _fired;

    /// <summary>True once the action has run.</summary>
    public bool Fired => Volatile.Read(ref _fired) == 1;

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var isClaim = eventData.Context is { } context
            && context.ChangeTracker.Entries<ReminderDelivery>()
                .Any(entry => entry.State == EntityState.Added && entry.Entity.UserId == userId);

        if (isClaim && Interlocked.Exchange(ref _fired, 1) == 0)
        {
            await action(cancellationToken);
        }

        return result;
    }
}
