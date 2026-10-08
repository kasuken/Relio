using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Relio.Application.Billing;

namespace Relio.Web.Billing;

/// <summary>
/// Registers billing dependencies based on application configuration.
/// </summary>
public static class BillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IBillingProvider"/> according to the <c>Billing</c> configuration section.
    /// When <see cref="BillingProviderType.None"/> (the default), registers <see cref="NullBillingProvider"/>
    /// which runs completely without payment gating or third-party connections.
    /// When <see cref="BillingProviderType.Stripe"/>, registers <see cref="StripeBillingProvider"/>.
    /// </summary>
    public static IServiceCollection AddRelioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<BillingOptions>(configuration.GetSection(BillingOptions.SectionName));

        var options = configuration
            .GetSection(BillingOptions.SectionName)
            .Get<BillingOptions>() ?? new BillingOptions();

        if (options.Provider == BillingProviderType.Stripe)
        {
            services.AddHttpClient<IBillingProvider, StripeBillingProvider>();
        }
        else
        {
            services.AddSingleton<IBillingProvider, NullBillingProvider>();
        }

        return services;
    }
}
