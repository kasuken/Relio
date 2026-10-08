using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Relio.Application.Accounts;
using Relio.Application.Administration;
using Relio.Application.Dashboard;
using Relio.Application.DifficultMoments;
using Relio.Application.Interactions;
using Relio.Application.Metrics;
using Relio.Application.Notes;
using Relio.Application.Onboarding;
using Relio.Application.People;
using Relio.Application.People.Import;
using Relio.Application.Portability;
using Relio.Application.Timeline;
using Relio.Application.Reminders;
using Relio.Data.Accounts;
using Relio.Data.Administration;
using Relio.Data.Concurrency;
using Relio.Data.Dashboard;
using Relio.Data.DifficultMoments;
using Relio.Data.Encryption;
using Relio.Data.Identity;
using Relio.Data.Interactions;
using Relio.Data.Metrics;
using Relio.Data.Notes;
using Relio.Data.Onboarding;
using Relio.Application.Profile;
using Relio.Application.Time;
using Relio.Data.People;
using Relio.Data.Portability;
using Relio.Data.Profile;
using Relio.Data.Reminders;
using Relio.Data.Seeding;
using Relio.Data.Time;
using Relio.Data.Timeline;

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
        services.TryAddSingleton<UserDataPortabilityRestoreConcurrencyInterceptor>();

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
        services.AddFieldProtection();
        services.AddOptions<ProductMetricsOptions>()
            .Bind(configuration.GetSection(ProductMetricsOptions.SectionName))
            .ValidateOnStart();
        services.TryAddSingleton<IProductMetricsCollectionGate, ProductMetricsCollectionGate>();

        AddDataService<IPeopleService, PeopleService>(services);
        AddDataService<IDashboardService, DashboardService>(services);
        AddDataService<IOnboardingService, OnboardingService>(services);
        AddDataService<IInteractionService, InteractionService>(services);
        AddDataService<INoteService, NoteService>(services);
        AddDataService<IPersonTimelineService, PersonTimelineService>(services);
        AddDataService<IPeopleImportService, PeopleImportService>(services);
        AddDataService<IPersonMergeService, PersonMergeService>(services);
        AddDataService<IRelationshipTypeService, RelationshipTypeService>(services);
        AddDataService<ITagService, TagService>(services);
        AddDataService<IReminderService, ReminderService>(services);
        AddDataService<IDifficultMomentService, DifficultMomentService>(services);
        AddDataService<INotificationPreferencesService, NotificationPreferencesService>(services);
        AddDataService<IUnsubscribeService, UnsubscribeService>(services);
        AddDataService<IReminderSchedulerRunner, ReminderSchedulerRunner>(services);
        AddDataService<IUserTimeZoneService, UserTimeZoneService>(services);
        AddDataService<IUserProfileService, UserProfileService>(services);
        AddDataService<ITwoFactorStatusService, TwoFactorStatusService>(services);
        AddDataService<IUserDataPortabilityService, UserDataPortabilityService>(services);
        AddDataService<IAccountDeletionService, AccountDeletionService>(services);
        AddDataService<IAccountSessionStatusService, AccountSessionStatusService>(services);
        AddDataService<IProductActivityService, ProductActivityService>(services);
        AddDataService<IProductMetricsReportService, ProductMetricsReportService>(services);
        AddDataService<IProductMetricsRetentionRunner, ProductMetricsRetentionRunner>(services);

        // Self-hosted administration (issue #19). RegistrationLock is a singleton on purpose: it
        // serializes registrations process-wide (see AccountRegistrationService's remarks). Both
        // services depend on UserManager<RelioUser>, registered by Relio.Web's AddRelioIdentity, and
        // on IOptions<RegistrationOptions>, which AddRelioIdentity also configures (fail-fast parsing).
        services.AddSingleton<RegistrationLock>();
        AddDataService<IAccountRegistrationService, AccountRegistrationService>(services);
        AddDataService<IUserAdministrationService, UserAdministrationService>(services);
        services.Configure<AdministrationOptions>(configuration.GetSection(AdministrationOptions.SectionName));
        services.AddScoped<AdministratorBootstrapper>();

        // DemoDataSeeder depends on UserManager<RelioUser>, registered by Relio.Web's
        // AddRelioIdentity - that's fine, DI only needs it present by the time Program.cs resolves
        // this from a scope, not at registration time. See DemoDataOptions for the Enabled flag.
        services.Configure<DemoDataOptions>(configuration.GetSection(DemoDataOptions.SectionName));
        services.AddScoped<DemoDataSeeder>();

        return services;
    }

    /// <summary>
    /// Registers a data service (anything that uses <see cref="RelioDbContext"/> or Identity's
    /// <c>UserManager</c>) so every call runs in its context's <see cref="DatabaseLane"/>. Every
    /// data service goes through this, never a plain <c>AddScoped</c> (<c>DataServiceRegistrationTests</c>
    /// enforces it). See "One database operation at a time" in AGENTS.md.
    /// </summary>
    private static void AddDataService<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        // Fails at startup, not on first call, if the interface has a member the lane cannot queue.
        DatabaseLaneProxy<TService>.ThrowIfUnsupported();

        var create = ActivatorUtilities.CreateFactory<TImplementation>(Type.EmptyTypes);
        services.AddScoped<TService>(provider => DatabaseLaneProxy<TService>.Create(
            create(provider, null),
            provider.GetRequiredService<RelioDbContext>().Lane));
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

        // SingleQuery stated outright: loading a person with both its tags and its contact methods
        // is two collection includes, and EF Core logs a warning for that unless a behaviour is
        // configured. One query is right here (at most 20 of each, so a few hundred rows) and
        // consistent; queries never call AsSingleQuery/AsSplitQuery themselves because those are
        // relational-only and the E2E tests run on the InMemory provider.
        services.AddDbContext<RelioDbContext>((provider, options) => options.UseSqlServer(
            connectionString,
            sqlServerOptions =>
            {
                sqlServerOptions.MigrationsAssembly(typeof(RelioDbContext).Assembly.FullName);
                sqlServerOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
            }).AddInterceptors(provider.GetRequiredService<UserDataPortabilityRestoreConcurrencyInterceptor>()));
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
