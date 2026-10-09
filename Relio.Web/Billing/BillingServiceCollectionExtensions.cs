using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Relio.Application.Billing;

namespace Relio.Web.Billing;

/// <summary>Registers hosted billing according to the <c>Billing</c> configuration section.</summary>
public static class BillingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IBillingProvider"/>. <see cref="BillingProviderType.None"/> (the default)
    /// registers <see cref="NullBillingProvider"/>: no plans, no limits, no payment UI and no
    /// connection to any provider. <see cref="BillingProviderType.Stripe"/> validates every required
    /// setting first - startup fails with one message naming each missing one - and registers
    /// <see cref="StripeBillingProvider"/>, a singleton because it holds no per-user state.
    /// </summary>
    public static IServiceCollection AddRelioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(BillingOptions.SectionName);
        services.Configure<BillingOptions>(section);

        var options = section.Get<BillingOptions>() ?? new BillingOptions();
        options.Validate();

        switch (options.Provider)
        {
            case BillingProviderType.None:
                services.AddSingleton<IBillingProvider, NullBillingProvider>();
                break;

            case BillingProviderType.Stripe:
                services.AddSingleton<IBillingProvider, StripeBillingProvider>();
                break;

            default:
                throw new InvalidOperationException(
                    $"Billing provider '{options.Provider}' is not supported. Use 'None' or 'Stripe'.");
        }

        return services;
    }
}
