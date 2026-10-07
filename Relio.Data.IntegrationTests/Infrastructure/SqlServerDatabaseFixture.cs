using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Relio.Data.IntegrationTests.Infrastructure;

/// <summary>
/// Creates a uniquely named database on the SQL Server instance named by
/// <see cref="SqlServerTestEnvironment.ServerConnectionString"/>, applies the real EF Core
/// migrations to it with <see cref="DatabaseFacade.MigrateAsync"/> (proving the migrations
/// themselves, not just that the current model is reachable), and drops the database again when
/// the test run finishes.
/// </summary>
/// <remarks>
/// Shared once per test run via <see cref="SqlServerCollection"/> rather than created per test
/// class or per test method: migrating a database costs real time against a real SQL Server
/// instance, and every Relio service already scopes every query and mutation to
/// <c>IOwnedEntity.OwnerId</c> (see the "User-scoped data pattern" section of AGENTS.md), so tests
/// stay isolated from each other simply by seeding with a fresh random owner id per test
/// (<see cref="TestDataFactory.NewOwnerId"/>) - no two tests ever read or write the same row, even
/// though they share one database and one schema.
/// <para>
/// When <see cref="SqlServerTestEnvironment.IsAvailable"/> is <see langword="false"/> (e.g. this
/// machine has no Docker/SQL Server access), <see cref="InitializeAsync"/> and
/// <see cref="DisposeAsync"/> are no-ops; every test guarded by <see cref="SqlServerFactAttribute"/>
/// is skipped instead, so nothing ever calls <see cref="CreateDbContext"/>.
/// </para>
/// </remarks>
public sealed class SqlServerDatabaseFixture : IAsyncLifetime
{
    private string? _databaseConnectionString;

    /// <summary>
    /// This run's database connection string, for tests that build the real dependency injection
    /// registration (<c>AddRelioData</c>) against it. Throws if no SQL Server instance was
    /// available - guard the test with <see cref="SqlServerFactAttribute"/>.
    /// </summary>
    public string ConnectionString => _databaseConnectionString ?? throw new InvalidOperationException(
        $"{nameof(SqlServerDatabaseFixture)} has no database because " +
        $"{SqlServerTestEnvironment.ConnectionStringEnvironmentVariable} was not set. Guard " +
        $"the test with [{nameof(SqlServerFactAttribute)}] so it is skipped instead of run.");

    public async Task InitializeAsync()
    {
        var serverConnectionString = SqlServerTestEnvironment.ServerConnectionString;
        if (string.IsNullOrWhiteSpace(serverConnectionString))
        {
            return;
        }

        var builder = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = $"Relio_IntegrationTests_{Guid.NewGuid():N}",
        };
        _databaseConnectionString = builder.ConnectionString;

        await using var dbContext = CreateDbContext();
        await SqlServerTestDatabase.CreateAndMigrateAsync(dbContext);
    }

    public async Task DisposeAsync()
    {
        if (_databaseConnectionString is null)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    /// <summary>
    /// Creates a new <see cref="RelioDbContext"/> against this run's database, using
    /// <see cref="TimeProvider.System"/> for audit timestamps. Throws if no SQL Server instance
    /// was available for this run - callers should guard the test with
    /// <see cref="SqlServerFactAttribute"/> so that never happens. <paramref name="interceptors"/>
    /// are added to the context, for tests that need to do something at a precise moment (a
    /// competing write between a read and the save) that two contexts alone cannot time.
    /// </summary>
    public RelioDbContext CreateDbContext(params IInterceptor[] interceptors)
    {
        if (_databaseConnectionString is null)
        {
            throw new InvalidOperationException(
                $"{nameof(SqlServerDatabaseFixture)} has no database because " +
                $"{SqlServerTestEnvironment.ConnectionStringEnvironmentVariable} was not set. Guard " +
                $"the test with [{nameof(SqlServerFactAttribute)}] so it is skipped instead of run.");
        }

        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseSqlServer(_databaseConnectionString)
            .AddInterceptors(interceptors)
            .Options;

        return new RelioDbContext(options, TimeProvider.System, FieldProtector);
    }
}
