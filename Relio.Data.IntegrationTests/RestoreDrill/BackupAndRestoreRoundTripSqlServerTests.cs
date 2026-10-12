using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Relio.Data.Encryption;
using Relio.Data.IntegrationTests.Infrastructure;

namespace Relio.Data.IntegrationTests.RestoreDrill;

/// <summary>
/// The database half of the restore drill, proven against any SQL Server (CI's service container or
/// a local LocalDB, no Docker needed): a native <c>BACKUP DATABASE</c> of a migrated database that
/// holds protected rows is restored under another name, and the restored rows decrypt with the same
/// key ring and only with it. The end-to-end drill (<c>restore-drill.yml</c>) adds the encrypted
/// archives, the application and a separate environment on top of this.
/// </summary>
[Trait("Category", "RestoreDrillSupport")]
public sealed class BackupAndRestoreRoundTripSqlServerTests
{
    [SqlServerFact]
    public async Task A_native_backup_restores_to_a_database_whose_protected_rows_decrypt_with_the_same_key_ring_only()
    {
        var applicationName = $"Relio.RoundTrip.{Guid.NewGuid():N}";
        var suffix = Guid.NewGuid().ToString("N");
        var sourceName = $"Relio_RoundTrip_{suffix}";
        var restoredName = $"Relio_RoundTripRestored_{suffix}";
        var canary = RestoreDrillCanary.Create(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        using var keyRing = DataProtectionTestHarness.CreateIsolatedKeyRing(applicationName);

        var server = new SqlConnectionStringBuilder(SqlServerTestEnvironment.ServerConnectionString!);
        var sourceConnection = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = sourceName }.ConnectionString;
        var restoredConnection = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = restoredName }.ConnectionString;
        var masterConnection = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        string? backupFile = null;

        try
        {
            await using (var sourceContext = new RelioDbContext(
                RestoreDrillData.CreateOptions(sourceConnection),
                TimeProvider.System,
                keyRing.FieldProtector))
            {
                await SqlServerTestDatabase.CreateAndMigrateAsync(sourceContext);
            }

            var seeded = await RestoreDrillData.SeedAsync(sourceConnection, keyRing.FieldProtector, canary);

            var paths = await ReadServerPathsAsync(masterConnection);
            backupFile = $"{paths.BackupDirectory}{sourceName}.bak";
            await ExecuteAsync(
                masterConnection,
                $"BACKUP DATABASE [{sourceName}] TO DISK = N'{backupFile}' WITH FORMAT, INIT, CHECKSUM");
            await ExecuteAsync(masterConnection, $"RESTORE VERIFYONLY FROM DISK = N'{backupFile}' WITH CHECKSUM");
            var files = await ReadBackupFilesAsync(masterConnection, backupFile);
            await ExecuteAsync(
                masterConnection,
                $"RESTORE DATABASE [{restoredName}] FROM DISK = N'{backupFile}' WITH "
                + $"MOVE N'{files.DataName}' TO N'{paths.DataDirectory}{restoredName}.mdf', "
                + $"MOVE N'{files.LogName}' TO N'{paths.DataDirectory}{restoredName}_log.ldf', CHECKSUM, RECOVERY");

            // The restored database decrypts with the same key ring: nothing to convert, canary exact.
            using var sameKeys = keyRing.Restart();
            var service = new SensitiveFieldBackfillService(
                RestoreDrillData.CreateOptions(restoredConnection),
                TimeProvider.System,
                sameKeys.FieldProtector);
            (await service.RunAsync()).Should().Be(new SensitiveFieldBackfillResult(0, 0, 0, 0, 0, 0));
            (await RestoreDrillData.VerifyCanaryAsync(restoredConnection, sameKeys.FieldProtector, canary))
                .Should().Be(canary.ProtectedValues.Count);
            (await RestoreDrillData.CountRowsAsync(restoredConnection, sameKeys.FieldProtector))
                .Should().BeEquivalentTo(seeded.RowCounts);
            (await RestoreDrillData.ReadLatestMigrationAsync(restoredConnection))
                .Should().Be(seeded.LatestMigrationId);

            // A key ring without the original keys cannot read it, so the restore fails closed.
            using var otherKeys = DataProtectionTestHarness.CreateIsolatedKeyRing(applicationName);
            var otherService = new SensitiveFieldBackfillService(
                RestoreDrillData.CreateOptions(restoredConnection),
                TimeProvider.System,
                otherKeys.FieldProtector);
            var otherRun = () => otherService.RunAsync();
            await otherRun.Should().ThrowAsync<SensitiveFieldBackfillException>();
        }
        finally
        {
            await DropAsync(masterConnection, sourceName);
            await DropAsync(masterConnection, restoredName);
            DeleteBackupFile(backupFile);
        }
    }

    private sealed record ServerPaths(string BackupDirectory, string DataDirectory);

    private sealed record BackupFiles(string DataName, string LogName);

    private static async Task<ServerPaths> ReadServerPathsAsync(string masterConnection)
    {
        await using var connection = new SqlConnection(masterConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(260)), "
            + "COALESCE(CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(260)), "
            + "CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(260)))";
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return new ServerPaths(BackupDirectory: reader.GetString(1), DataDirectory: reader.GetString(0));
    }

    private static async Task<BackupFiles> ReadBackupFilesAsync(string masterConnection, string backupFile)
    {
        string? data = null;
        string? log = null;
        await using var connection = new SqlConnection(masterConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"RESTORE FILELISTONLY FROM DISK = N'{backupFile}'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var logicalName = reader.GetString(0);
            var type = reader.GetString(2);
            if (type == "D")
            {
                data ??= logicalName;
            }
            else if (type == "L")
            {
                log ??= logicalName;
            }
        }

        return new BackupFiles(data ?? throw new InvalidOperationException("No data file."), log ?? throw new InvalidOperationException("No log file."));
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 180;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropAsync(string masterConnection, string databaseName)
    {
        try
        {
            await ExecuteAsync(
                masterConnection,
                $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END");
        }
        catch (SqlException)
        {
            // Best effort cleanup of a throwaway database; the test result must not depend on it.
        }
    }

    private static void DeleteBackupFile(string? backupFile)
    {
        // The file is on the server. With a local instance it is reachable here and removed; with a
        // remote or containerised server (CI) it is not, and the throwaway server is discarded.
        if (backupFile is null)
        {
            return;
        }

        try
        {
            File.Delete(backupFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }
}
