using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Relio.Data;

/// <summary>
/// Creates <see cref="RelioDbContext"/> instances for <c>dotnet ef</c> design-time operations
/// (adding migrations, generating scripts) without needing a reachable database or the full
/// Relio.Web host. A placeholder connection string is used only to satisfy the SQL Server
/// provider; no connection is ever opened for these operations.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RelioDbContext>
{
    /// <inheritdoc />
    public RelioDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RelioDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=.;Database=Relio;Trusted_Connection=True;TrustServerCertificate=True;",
            sqlServerOptions => sqlServerOptions.MigrationsAssembly(typeof(RelioDbContext).Assembly.FullName));

        return new RelioDbContext(optionsBuilder.Options);
    }
}
