using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Relio.Data.Tests.People;

/// <summary>Counts how many times a context saved, so a test can prove an operation is one save (one transaction on SQL Server).</summary>
internal sealed class CountingSaveChangesInterceptor : SaveChangesInterceptor
{
    public int Saves { get; private set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Saves++;
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
