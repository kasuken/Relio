namespace Relio.Data.IntegrationTests.Infrastructure;

/// <summary>
/// The xUnit collection every SQL Server integration test class belongs to, so they all share one
/// <see cref="SqlServerDatabaseFixture"/> (and so its one migration runs once per test run, not
/// once per test class).
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerDatabaseFixture>
{
    public const string Name = "SQL Server integration tests";
}
