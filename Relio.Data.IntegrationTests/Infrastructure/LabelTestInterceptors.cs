using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Relio.Data.IntegrationTests.Infrastructure;

/// <summary>
/// Runs <paramref name="action"/> just before the first save of the context it is attached to -
/// "somebody else did something at the same moment". It is the only way to hit the window between a
/// service reading rows and saving what it decided, which two sequential calls can never reach.
/// Whatever the action does must go through a <b>separate</b> context.
/// </summary>
internal sealed class RunOnFirstSaveInterceptor(Func<CancellationToken, Task> action) : SaveChangesInterceptor
{
    private int _fired;

    /// <summary>True once the action has run.</summary>
    public bool Fired => _fired > 0;

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _fired) == 1)
        {
            await action(cancellationToken);
        }

        return result;
    }
}

/// <summary>Counts the saves that reached the database, to prove a multi-step write is one save.</summary>
internal sealed class CountSavesInterceptor : SaveChangesInterceptor
{
    private int _saved;

    /// <summary>How many <c>SaveChanges</c> calls completed.</summary>
    public int Saved => _saved;

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _saved);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}

/// <summary>
/// Makes the command that deletes from <paramref name="table"/> fail, as if the database refused it,
/// so a test can prove everything before it in the same save is rolled back.
/// </summary>
internal sealed class FailDeleteFromInterceptor(string table) : DbCommandInterceptor
{
    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains($"DELETE FROM [{table}]", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("simulated database failure");
        }

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
