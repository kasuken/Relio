using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Relio.Application.People;
using Relio.Data.People;

namespace Relio.Data.DependencyInjection;

/// <summary>
/// Registers the Relio data layer (EF Core <see cref="RelioDbContext"/> and the SQL Server
/// provider) with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The name of the connection string Relio reads from configuration
    /// (<c>ConnectionStrings:Relio</c>).
    /// </summary>
    public const string ConnectionStringName = "Relio";

    /// <summary>
    /// Adds <see cref="RelioDbContext"/> to the service collection, configured for SQL Server
    /// using the <c>ConnectionStrings:Relio</c> connection string.
    /// </summary>
    /// <param name="services">The service collection to add the data layer to.</param>
    /// <param name="configuration">
    /// The application configuration, used to read the <c>ConnectionStrings:Relio</c> value.
    /// Populate it locally with
    /// <c>dotnet user-secrets set ConnectionStrings:Relio "&lt;connection string&gt;"</c>, or set
    /// the <c>ConnectionStrings__Relio</c> environment variable when hosting.
    /// </param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// The <c>ConnectionStrings:Relio</c> value is missing or empty.
    /// </exception>
    public static IServiceCollection AddRelioData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is missing. Set it with " +
                $"'dotnet user-secrets set ConnectionStrings:{ConnectionStringName} \"<connection string>\"' " +
                $"locally, or the 'ConnectionStrings__{ConnectionStringName}' environment variable when hosting.");
        }

        services.AddDbContext<RelioDbContext>(options => options.UseSqlServer(
            connectionString,
            sqlServerOptions => sqlServerOptions.MigrationsAssembly(typeof(RelioDbContext).Assembly.FullName)));

        // TryAdd: Relio.Web (and tests) may register a different TimeProvider (e.g. a fake for
        // deterministic tests); this just guarantees one is always available for audit
        // timestamps.
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IPeopleService, PeopleService>();

        return services;
    }
}
