using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Relio.Data.Encryption;
using Relio.Data.IntegrationTests.Infrastructure;

namespace Relio.Data.IntegrationTests.Encryption;

internal sealed class SensitiveFieldSqlServerDatabase : IAsyncLifetime
{
    private string? _connectionString;

    public string ConnectionString => _connectionString
        ?? throw new InvalidOperationException(
            $"A SQL Server database is unavailable. Set {SqlServerTestEnvironment.ConnectionStringEnvironmentVariable}.");

    public async Task InitializeAsync()
    {
        var serverConnectionString = SqlServerTestEnvironment.ServerConnectionString;
        if (string.IsNullOrWhiteSpace(serverConnectionString))
        {
            return;
        }

        var builder = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = $"Relio_SensitiveFields_{Guid.NewGuid():N}",
        };
        _connectionString = builder.ConnectionString;

        await using var dbContext = CreateDbContext();
        await SqlServerTestDatabase.CreateAndMigrateAsync(dbContext);
    }

    public async Task DisposeAsync()
    {
        if (_connectionString is null)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    public RelioDbContext CreateDbContext(
        IDataProtectionFieldProtector? protector = null,
        params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(interceptors)
            .Options;

        return new RelioDbContext(
            options,
            TimeProvider.System,
            protector ?? DataProtectionTestHarness.FieldProtector);
    }
}
