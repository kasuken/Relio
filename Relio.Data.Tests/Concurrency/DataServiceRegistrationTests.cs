using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Relio.Application.Reminders;
using Relio.Application.Security;
using Relio.Data.Administration;
using Relio.Data.Concurrency;
using Relio.Data.DependencyInjection;
using Relio.Data.Encryption;
using Relio.Data.Identity;
using Relio.Data.Seeding;
using Relio.Data.Tests.People;

namespace Relio.Data.Tests.Concurrency;

/// <summary>
/// The guard rails of the database lane: a data service registered with a plain
/// <c>AddScoped</c> would silently bypass it, and the lane is what stops sibling Blazor components
/// from colliding on the circuit's shared <see cref="RelioDbContext"/>. See "One database operation
/// at a time" in AGENTS.md.
/// </summary>
public class DataServiceRegistrationTests
{
    [Fact]
    public void Every_application_service_AddRelioData_registers_runs_in_the_database_lane()
    {
        var (services, added) = BuildServices();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var applicationServices = added
            .Where(d => d.ServiceType.IsInterface && d.ServiceType.Assembly == typeof(ICurrentUser).Assembly)
            .Select(d => d.ServiceType)
            .Distinct()
            .ToList();

        applicationServices.Should().NotBeEmpty();
        foreach (var type in applicationServices)
        {
            // Resolving every one also proves the dependency graph builds.
            scope.ServiceProvider.GetRequiredService(type).Should().BeAssignableTo<IDatabaseLaneProxy>(type.Name);
        }
    }

    [Fact]
    public void No_class_that_uses_the_context_is_registered_outside_the_lane()
    {
        var (_, added) = BuildServices();

        // Startup-only: resolved once in their own scope by Program.cs, never by a component.
        Type[] allowed = [typeof(DemoDataSeeder), typeof(AdministratorBootstrapper)];

        var offenders = added
            .Where(d => d.ImplementationType is { } type
                && !allowed.Contains(type)
                && type.GetConstructors()
                    .SelectMany(c => c.GetParameters())
                    .Any(p => p.ParameterType == typeof(RelioDbContext)
                        || p.ParameterType == typeof(UserManager<RelioUser>)))
            .Select(d => d.ImplementationType!.Name)
            .ToList();

        offenders.Should().BeEmpty("data services must be registered with AddDataService (see AGENTS.md)");
    }

    private static (ServiceCollection Services, List<ServiceDescriptor> Added) BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser("user"));
        services.AddSingleton<IReminderEmailSender>(new Reminders.FakeReminderEmailSender());
        services.AddSingleton<Relio.Application.Billing.IBillingProvider>(new Relio.Application.Billing.NullBillingProvider());
        services.AddSingleton<IDataProtectionFieldProtector>(FieldProtector);
        services.AddIdentityCore<RelioUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<RelioDbContext>();
        var before = services.ToList();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ServiceCollectionExtensions.ProviderConfigurationKey] = ServiceCollectionExtensions.InMemoryProvider,
        }).Build();
        services.AddRelioData(configuration);

        return (services, services.Except(before).ToList());
    }
}
