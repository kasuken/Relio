using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Relio.Data.IntegrationTests.Infrastructure;

/// <summary>
/// Holds every query open for a moment before SQL Server runs it. EF Core enters its concurrency
/// check at the start of an operation and keeps it until the operation ends, so this runs inside
/// that critical section: the first operation is guaranteed to still be in flight when a second one
/// starts, which makes an overlap deterministic instead of a matter of timing. The delay is
/// asynchronous, exactly like the real I/O wait that lets Blazor's renderer start a sibling
/// component's load.
/// </summary>
internal sealed class SlowReaderInterceptor(TimeSpan delay) : DbCommandInterceptor
{
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(delay, cancellationToken);
        return result;
    }
}
