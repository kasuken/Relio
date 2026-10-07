using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Relio.Application.People;
using Relio.Application.Portability;
using Relio.Domain;

namespace Relio.Data.Portability;

internal static class UserDataPortabilityRestoreCheck
{
    private static readonly ConditionalWeakTable<RelioDbContext, RestoreRequest> Requests = new();

    public static IDisposable Arm(RelioDbContext dbContext, string ownerId)
    {
        if (!dbContext.Database.IsSqlServer())
        {
            throw new InvalidOperationException("The SQL restore freshness check requires SQL Server.");
        }

        var request = new RestoreRequest(ownerId);
        Requests.Add(dbContext, request);
        return new Scope(dbContext);
    }

    public static bool IsArmed(DbContext? context) =>
        context is RelioDbContext dbContext && Requests.TryGetValue(dbContext, out _);

    public static RestoreRequest? GetRequest(DbContext? context) =>
        context is RelioDbContext dbContext && Requests.TryGetValue(dbContext, out var request)
            ? request
            : null;

    internal sealed class RestoreRequest(string ownerId)
    {
        private readonly object _sync = new();
        private DbTransaction? _transaction;

        public string OwnerId { get; } = ownerId;

        public bool TryStart(DbTransaction transaction)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_transaction, transaction))
                {
                    return false;
                }

                _transaction = transaction;
                return true;
            }
        }
    }

    private sealed class Scope(RelioDbContext dbContext) : IDisposable
    {
        public void Dispose() => Requests.Remove(dbContext);
    }
}

/// <summary>
/// Acquires a transaction-owned per-account application lock, then rechecks freshness
/// before the first command in the implicit SaveChanges transaction.
/// </summary>
/// <remarks>
/// Ordinary writes are unaffected; the restore service arms this interceptor only around its one SQL save.
/// </remarks>
public sealed class UserDataPortabilityRestoreConcurrencyInterceptor : DbCommandInterceptor
{
    private const int LockTimeoutMilliseconds = 15_000;
    private const string OwnerLockResourcePrefix = "Relio.UserDataRestore.v1.";

    private const string HasOwnedContentSql = """
        SELECT CASE WHEN
            EXISTS (SELECT 1 FROM [People] WHERE [OwnerId] = @ownerId)
            OR EXISTS (SELECT 1 FROM [ContactMethods] WHERE [OwnerId] = @ownerId)
            OR EXISTS (SELECT 1 FROM [Tags] WHERE [OwnerId] = @ownerId)
            OR EXISTS (SELECT 1 FROM [Interactions] WHERE [OwnerId] = @ownerId)
            OR EXISTS (SELECT 1 FROM [InteractionParticipants] WHERE [OwnerId] = @ownerId)
            OR EXISTS (SELECT 1 FROM [Notes] WHERE [OwnerId] = @ownerId)
            OR EXISTS (SELECT 1 FROM [Reminders] WHERE [OwnerId] = @ownerId)
            THEN 1 ELSE 0 END;
        """;

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        RejectSynchronousRestore(eventData);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        RejectSynchronousRestore(eventData);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        RejectSynchronousRestore(eventData);
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await GuardFirstRestoreCommandAsync(command, eventData, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await GuardFirstRestoreCommandAsync(command, eventData, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await GuardFirstRestoreCommandAsync(command, eventData, cancellationToken);
        return result;
    }

    private static void RejectSynchronousRestore(CommandEventData eventData)
    {
        if (UserDataPortabilityRestoreCheck.IsArmed(eventData.Context))
        {
            throw new InvalidOperationException("Data restore writes must use asynchronous database commands.");
        }
    }

    private static async Task GuardFirstRestoreCommandAsync(
        DbCommand command,
        CommandEventData eventData,
        CancellationToken cancellationToken)
    {
        var request = UserDataPortabilityRestoreCheck.GetRequest(eventData.Context);
        if (request is null)
        {
            return;
        }

        var connection = command.Connection
            ?? throw new InvalidOperationException("The restore command has no database connection.");
        var transaction = command.Transaction
            ?? (eventData.Context as RelioDbContext)?.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException(
                "The restore freshness check requires EF Core's implicit SaveChanges transaction.");
        if (!request.TryStart(transaction))
        {
            return;
        }

        await AcquireTransactionLockAsync(
            connection,
            transaction,
            GetOwnerLockResource(request.OwnerId),
            cancellationToken);
        if (!await IsDestinationFreshAsync(connection, transaction, request.OwnerId, cancellationToken))
        {
            throw new UserDataPortabilityException([UserDataPortabilityError.DestinationNotFresh]);
        }
    }

    private static async Task AcquireTransactionLockAsync(
        DbConnection connection,
        DbTransaction transaction,
        string resource,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = @lockTimeout;
            SELECT @result;
            """;
        command.CommandTimeout = (LockTimeoutMilliseconds / 1000) + 5;
        AddParameter(command, "@resource", DbType.String, 255, resource);
        AddParameter(command, "@lockTimeout", DbType.Int32, 0, LockTimeoutMilliseconds);

        var result = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        if (result >= 0)
        {
            return;
        }

        if (result == -2 && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        throw new UserDataPortabilityRestoreLockException();
    }

    private static async Task<bool> IsDestinationFreshAsync(
        DbConnection connection,
        DbTransaction transaction,
        string ownerId,
        CancellationToken cancellationToken)
    {
        await using (var contentCommand = connection.CreateCommand())
        {
            contentCommand.Transaction = transaction;
            contentCommand.CommandText = HasOwnedContentSql;
            AddParameter(contentCommand, "@ownerId", DbType.String, 450, ownerId);
            var hasContent = Convert.ToInt32(
                await contentCommand.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
            if (hasContent != 0)
            {
                return false;
            }
        }

        var defaultNames = RelationshipType.DefaultNames
            .Select(LabelNameRules.Normalize)
            .OfType<string>()
            .ToHashSet(LabelNameRules.Comparer);
        var seenNames = new HashSet<string>(LabelNameRules.Comparer);
        await using var typeCommand = connection.CreateCommand();
        typeCommand.Transaction = transaction;
        typeCommand.CommandText = "SELECT [Name] FROM [RelationshipTypes] WHERE [OwnerId] = @ownerId;";
        AddParameter(typeCommand, "@ownerId", DbType.String, 450, ownerId);
        await using var reader = await typeCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = LabelNameRules.Normalize(reader.GetString(0));
            if (name is null || !defaultNames.Contains(name) || !seenNames.Add(name))
            {
                return false;
            }
        }

        return true;
    }

    internal static string GetOwnerLockResource(string ownerId)
    {
        // The lock is stable across app instances without exposing the account id in SQL diagnostics.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ownerId));
        return OwnerLockResourcePrefix + Convert.ToHexString(hash);
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        DbType dbType,
        int size,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = dbType;
        if (size > 0)
        {
            parameter.Size = size;
        }

        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

internal sealed class UserDataPortabilityRestoreLockException()
    : Exception("The data restore could not acquire the account lock.");
