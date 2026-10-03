namespace Relio.Data.IntegrationTests.Infrastructure;

/// <summary>
/// Reads the SQL Server connection string used by this test run from the
/// <c>ConnectionStrings__Relio</c> environment variable (the same variable CI sets at the job
/// level and <c>Relio.Data.ServiceCollectionExtensions.AddRelioData</c> reads in production).
/// </summary>
/// <remarks>
/// Local development machines may have no Docker/SQL Server access at all, so these tests must
/// not fail when the variable is unset - they must be reported as skipped. See
/// <see cref="SqlServerFactAttribute"/>.
/// </remarks>
internal static class SqlServerTestEnvironment
{
    public const string ConnectionStringEnvironmentVariable = "ConnectionStrings__Relio";

    public const string SkipReason =
        "ConnectionStrings__Relio is not set, so no SQL Server instance is available in this " +
        "environment. Start one locally and set the variable to run these tests, e.g.: " +
        "docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=\"Your_password123!\" -p 1433:1433 " +
        "mcr.microsoft.com/mssql/server:2022-latest, then " +
        "ConnectionStrings__Relio=\"Server=localhost,1433;Database=Relio;User Id=sa;" +
        "Password=Your_password123!;Encrypt=False;TrustServerCertificate=True;\" dotnet test.";

    /// <summary>
    /// The connection string to the SQL Server instance to run integration tests against, or
    /// <see langword="null"/> when <c>ConnectionStrings__Relio</c> is not set.
    /// </summary>
    public static string? ServerConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);

    /// <summary>Whether a SQL Server instance is available for this test run.</summary>
    public static bool IsAvailable => !string.IsNullOrWhiteSpace(ServerConnectionString);
}
