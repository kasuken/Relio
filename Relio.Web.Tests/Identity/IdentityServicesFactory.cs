using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Relio.Data.DependencyInjection;
using Relio.Web.Identity;
using DataServiceCollectionExtensions = Relio.Data.DependencyInjection.ServiceCollectionExtensions;
using IdentityServiceCollectionExtensions = Relio.Web.Identity.ServiceCollectionExtensions;

namespace Relio.Web.Tests.Identity;

/// <summary>
/// Builds the app's real Identity registration (<c>AddRelioData</c> on the InMemory provider plus
/// <c>AddRelioIdentity</c>) in a plain service provider, so tests exercise the same wiring - roles,
/// the custom sign-in manager, policies - the running app uses.
/// </summary>
internal static class IdentityServicesFactory
{
    public static ServiceProvider Build(IReadOnlyDictionary<string, string?>? configuration = null)
    {
        var values = new Dictionary<string, string?> { [DataServiceCollectionExtensions.ProviderConfigurationKey] = "InMemory" };
        foreach (var (key, value) in configuration ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var environment = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" }).Environment;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        // A plain accessor, not the default AsyncLocal one: tests set the context inside async helpers,
        // whose AsyncLocal changes do not flow back to the calling test method.
        services.AddSingleton<IHttpContextAccessor, PlainHttpContextAccessor>();
        services.AddRelioData(config);
        IdentityServiceCollectionExtensions.AddRelioIdentity(services, config, environment);
        return services.BuildServiceProvider();
    }

    /// <summary>A scope whose <c>HttpContext</c> is a plain <see cref="DefaultHttpContext"/> wired to that scope.</summary>
    public static IServiceScope CreateScopeWithHttpContext(ServiceProvider provider)
    {
        var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        return scope;
    }
}

internal sealed class PlainHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }
}
