using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Relio.Web.Security;

/// <summary>Registers active-circuit account-session revocation infrastructure.</summary>
public static class AccountSessionRevocationServiceCollectionExtensions
{
    /// <summary>
    /// Adds the process-local revocation registry and a scoped circuit handler. The handler also
    /// checks fresh account status on every inbound circuit activity, covering other instances.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddAccountSessionRevocation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAccountSessionRevocationNotifier, AccountSessionRevocationNotifier>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<CircuitHandler, AccountSessionCircuitHandler>());
        return services;
    }
}
