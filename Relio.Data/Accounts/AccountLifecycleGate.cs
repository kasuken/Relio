using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Relio.Data;

namespace Relio.Data.Accounts;

/// <summary>
/// Serializes account lifecycle changes in-process and across SQL Server instances.
/// </summary>
/// <remarks>
/// SQL Server's session-owned application lock is acquired before lifecycle reads and held through
/// the single <c>SaveChangesAsync</c>. This is deliberately outside a transaction: EF Core still
/// owns the one implicit transaction for the write batch, while the lock also protects the fresh
/// administrator checks that must happen before that batch. Closing the connection releases the
/// lock if the explicit release itself fails. InMemory uses the same in-process gate.
/// </remarks>
internal static class AccountLifecycleGate
{
    private const string LockResource = "Relio.AccountLifecycle";
    private const int LockTimeoutMilliseconds = 15_000;
    private static readonly SemaphoreSlim ProcessGate = new(1, 1);

    /// <summary>Enters the process and database-wide account lifecycle critical section.</summary>
    public static async ValueTask<IAsyncDisposable> EnterAsync(
        RelioDbContext dbContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        await ProcessGate.WaitAsync(cancellationToken);

        var connectionOpened = false;
        try
        {
            if (dbContext.Database.IsSqlServer())
            {
                if (dbContext.Database.CurrentTransaction is not null)
                {
                    throw new InvalidOperationException(
                        "Account lifecycle operations cannot run inside an existing transaction.");
                }

                var connection = dbContext.Database.GetDbConnection();
                if (connection.State != ConnectionState.Closed)
                {
                    throw new InvalidOperationException(
                        "Account lifecycle operations require a closed database connection.");
                }

                await dbContext.Database.OpenConnectionAsync(cancellationToken);
                connectionOpened = true;
                await AcquireSqlServerLockAsync(dbContext, cancellationToken);
            }
            else if (!dbContext.Database.IsInMemory())
            {
                throw new InvalidOperationException(
                    "Account lifecycle locking is supported only by SQL Server and the InMemory test provider.");
            }

            return new LifecycleScope(dbContext, connectionOpened);
        }
        catch
        {
            try
            {
                if (connectionOpened)
                {
                    await dbContext.Database.CloseConnectionAsync();
                }
            }
            finally
            {
                ProcessGate.Release();
            }

            throw;
        }
    }

    private static async Task AcquireSqlServerLockAsync(
        RelioDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "DECLARE @result int; " +
            "EXEC @result = sys.sp_getapplock " +
            $"@Resource = N'{LockResource}', @LockMode = 'Exclusive', " +
            $"@LockOwner = 'Session', @LockTimeout = {LockTimeoutMilliseconds}; " +
            "SELECT @result;";
        command.CommandTimeout = (LockTimeoutMilliseconds / 1000) + 5;

        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (result >= 0)
        {
            return;
        }

        if (result == -2 && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        throw new AccountLifecycleConcurrencyException();
    }

    private static async Task ReleaseSqlServerLockAsync(RelioDbContext dbContext)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "DECLARE @result int; " +
            "EXEC @result = sys.sp_releaseapplock " +
            $"@Resource = N'{LockResource}', @LockOwner = 'Session'; " +
            "SELECT @result;";
        command.CommandTimeout = (LockTimeoutMilliseconds / 1000) + 5;

        var result = Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None), CultureInfo.InvariantCulture);
        if (result < 0)
        {
            throw new InvalidOperationException("The account lifecycle lock could not be released.");
        }
    }

    private sealed class LifecycleScope : IAsyncDisposable
    {
        private readonly RelioDbContext _dbContext;
        private bool _connectionOpened;

        public LifecycleScope(RelioDbContext dbContext, bool connectionOpened)
        {
            _dbContext = dbContext;
            _connectionOpened = connectionOpened;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_dbContext.Database.IsSqlServer())
                {
                    try
                    {
                        await ReleaseSqlServerLockAsync(_dbContext);
                    }
                    catch
                    {
                        if (_connectionOpened)
                        {
                            await _dbContext.Database.CloseConnectionAsync();
                            _connectionOpened = false;
                        }

                        throw;
                    }
                }
            }
            finally
            {
                try
                {
                    if (_connectionOpened)
                    {
                        await _dbContext.Database.CloseConnectionAsync();
                    }
                }
                finally
                {
                    ProcessGate.Release();
                }
            }
        }
    }
}

/// <summary>A lifecycle operation could not acquire the cross-process serialization lock.</summary>
internal sealed class AccountLifecycleConcurrencyException()
    : Exception("A concurrent account lifecycle change is in progress")
{
}
