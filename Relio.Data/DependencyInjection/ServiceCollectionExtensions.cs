using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Relio.Application.Accounts;
using Relio.Application.Administration;
using Relio.Application.People;
using Relio.Data.Administration;
using Relio.Data.Identity;
using Relio.Application.Profile;
using Relio.Application.Time;
using Relio.Data.People;
using Relio.Data.Profile;
using Relio.Data.Seeding;
using Relio.Data.Time;

namespace Relio.Data.DependencyInjection;

/// <summary>
/// Registers the Relio data layer (EF Core <see cref="RelioDbContext"/> and its provider) with
/// the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The name of the connection string Relio reads from configuration
    /// (<c>ConnectionStrings:Relio</c>).
    /// </summary>
    public const string ConnectionStringName = "Relio";

    /// <summary>
    /// The configuration key that selects the EF Core provider (<c>Database:Provider</c>).
    /// </summary>
    public const string ProviderConfigurationKey = "Database:Provider";

    /// <summary>The default, production provider: SQL Server.</summary>
    public const string SqlServerProvider = "SqlServer";

    /// <summary>
    /// An in-process EF Core InMemory database. Test and local-dev only: it does not enforce
    /// SQL Server constraints, does not support migrations and is not durable. Never use it in
    /// a hosted or self-hosted production deployment.
    /// </summary>
    public const string InMemoryProvider = "InMemory";

    /// <summary>
    /// Adds <see cref="RelioDbContext"/> to the service collection, using the provider named by
    /// the <c>Database:Provider</c> configuration value (<see cref="SqlServerProvider"/>, the
    /// default, or <see cref="InMemoryProvider"/>).
    /// </summary>
    /// <param name="services">The service collection to add the data layer to.</param>
    /// <param name="configuration">
    /// The application configuration, used to read <c>Database:Provider</c> and, for the SQL
    /// Server provider, the <c>ConnectionStrings:Relio</c> value. Populate the connection string
    /// locally with
    /// <c>dotnet user-secrets set ConnectionStrings:Relio "&lt;connection string&gt;"</c>, or set
    /// the <c>ConnectionStrings__Relio</c> environment variable when hosting.
    /// </param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// The <c>ConnectionStrings:Relio</c> value is missing or empty for the SQL Server provider,
    /// or <c>Database:Provider</c> names a provider Relio does not recognise.
    /// </exception>
    public static IServiceCollection AddRelioData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var provider = GetProviderName(configuration);

        if (string.Equals(provider, InMemoryProvider, StringComparison.OrdinalIgnoreCase))
        {
            AddInMemoryDatabase(services);
        }
        else if (string.Equals(provider, SqlServerProvider, StringComparison.OrdinalIgnoreCase))
        {
            AddSqlServerDatabase(services, configuration);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unknown '{ProviderConfigurationKey}' value '{provider}'. Supported values are " +
                $"'{SqlServerProvider}' (the default) and '{InMemoryProvider}' (test/dev only).");
        }

        // TryAdd: Relio.Web (and tests) may register a different TimeProvider (e.g. a fake for
        // deterministic tests); this just guarantees one is always available for audit
        // timestamps.
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IPeopleService, PeopleService>();
        services.AddScoped<IUserTimeZoneService, UserTimeZoneService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<ITwoFactorStatusService, TwoFactorStatusService>();

        // Self-hosted administration (issue #19). RegistrationLock is a singleton on purpose: it
        // serializes registrations process-wide (see AccountRegistrationService's remarks). Both
        // services depend on UserManager<RelioUser>, registered by Relio.Web's AddRelioIdentity, and
        // on IOptions<RegistrationOptions>, which AddRelioIdentity also configures (fail-fast parsing).
        services.AddSingleton<RegistrationLock>();
        services.AddScoped<IAccountRegistrationService, AccountRegistrationService>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.Configure<AdministrationOptions>(configuration.GetSection(AdministrationOptions.SectionName));
        services.AddScoped<AdministratorBootstrapper>();

        // DemoDataSeeder depends on UserManager<RelioUser>, registered by Relio.Web's
        // AddRelioIdentity - that's fine, DI only needs it present by the time Program.cs resolves
        // this from a scope, not at registration time. See DemoDataOptions for the Enabled flag.
        services.Configure<DemoDataOptions>(configuration.GetSection(DemoDataOptions.SectionName));
        services.AddScoped<DemoDataSeeder>();

        return services;
    }

    private static void AddSqlServerDatabase(IServiceCollection services, IConfiguration configuration)
    {
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
    }

    private static void AddInMemoryDatabase(IServiceCollection services)
    {
        // One named database per process (not per DbContext instance), so every scope within a
        // test run or dev session shares the same data.
        var databaseName = $"Relio-InMemory-{Guid.NewGuid()}";

        services.AddDbContext<RelioDbContext>(options => options.UseInMemoryDatabase(databaseName));
    }

    /// <summary>
    /// Resolves the configured EF Core provider name, defaulting to <see cref="SqlServerProvider"/>
    /// when <c>Database:Provider</c> is not set.
    /// </summary>
    public static string GetProviderName(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var provider = configuration[ProviderConfigurationKey];
        return string.IsNullOrWhiteSpace(provider) ? SqlServerProvider : provider;
    }

    /// <summary>
    /// True when <c>Database:Provider</c> resolves to <see cref="InMemoryProvider"/> (test/dev
    /// only; see <see cref="InMemoryProvider"/>).
    /// </summary>
    public static bool IsInMemoryProvider(IConfiguration configuration) =>
        string.Equals(GetProviderName(configuration), InMemoryProvider, StringComparison.OrdinalIgnoreCase);
}
