using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Relio.Data.Health;
using Relio.Data.IntegrationTests.Infrastructure;

namespace Relio.Data.IntegrationTests.Health;

/// <summary>
/// Proves the readiness check against a real SQL Server database: a database that exists but has
/// no migrations applied is not ready, one that has been migrated is, and migrating again (what a
/// repeat deployment does on startup) leaves it ready. Uses its own throwaway database so the
/// shared fixture's migrated database is untouched.
/// </summary>
public sealed class PendingMigrationsHealthCheckSqlServerTests
{
    [SqlServerFact]
    public async Task Readiness_follows_the_migration_state_and_stays_healthy_when_migrating_again()
    {
        var builder = new SqlConnectionStringBuilder(SqlServerTestEnvironment.ServerConnectionString)
        {
            InitialCatalog = $"Relio_HealthCheck_{Guid.NewGuid():N}",
        };
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;

        await using var dbContext = new RelioDbContext(options, TimeProvider.System, FieldProtector);
        dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));
        try
        {
            // CreateAsync makes an empty database and runs no migrations.
            await dbContext.GetService<IRelationalDatabaseCreator>().CreateAsync();

            // Each health check registration gets its own scope (and context) in the real app.
            (await CheckAsync(options)).Status.Should().Be(HealthStatus.Unhealthy);

            await dbContext.Database.MigrateAsync();
            (await CheckAsync(options)).Status.Should().Be(HealthStatus.Healthy);

            // A repeat deployment runs MigrateAsync again with nothing pending.
            await dbContext.Database.MigrateAsync();
            (await CheckAsync(options)).Status.Should().Be(HealthStatus.Healthy);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<HealthCheckResult> CheckAsync(DbContextOptions<RelioDbContext> options)
    {
        await using var checkContext = new RelioDbContext(options, TimeProvider.System, FieldProtector);
        return await new PendingMigrationsHealthCheck(checkContext).CheckHealthAsync(new HealthCheckContext());
    }
}
