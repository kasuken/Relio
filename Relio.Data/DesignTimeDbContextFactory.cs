using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Relio.Data;

/// <summary>
/// Creates <see cref="RelioDbContext"/> instances for <c>dotnet ef</c> design-time operations
/// (adding migrations, generating scripts) without needing a reachable database or the full
/// Relio.Web host. Commands that touch a database (<c>dotnet ef database update</c>) use the
/// <c>ConnectionStrings__Relio</c> environment variable; otherwise a placeholder connection
/// string satisfies the SQL Server provider and no connection is opened.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RelioDbContext>
{
    /// <inheritdoc />
    public RelioDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Relio");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Server=.;Database=Relio;Trusted_Connection=True;TrustServerCertificate=True;";
        }

        var optionsBuilder = new DbContextOptionsBuilder<RelioDbContext>();
        optionsBuilder.UseSqlServer(
            connectionString,
            sqlServerOptions => sqlServerOptions.MigrationsAssembly(typeof(RelioDbContext).Assembly.FullName));

        return new RelioDbContext(optionsBuilder.Options);
    }
}
