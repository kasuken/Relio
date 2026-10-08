using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Relio.Application.Billing;
using Relio.Web.Billing;
using Xunit;

namespace Relio.Web.Tests.Billing;

public sealed class BillingDependencyInjectionTests
{
    [Fact]
    public void AddRelioBilling_defaults_to_NullBillingProvider_when_provider_is_none()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:Provider"] = "None",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddRelioBilling(configuration);
        var provider = services.BuildServiceProvider();

        var billing = provider.GetRequiredService<IBillingProvider>();
        billing.Should().BeOfType<NullBillingProvider>();
        billing.IsBillingEnabled.Should().BeFalse();
    }

    [Fact]
    public void AddRelioBilling_registers_StripeBillingProvider_when_provider_is_stripe()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:Provider"] = "Stripe",
                ["Billing:ApiKey"] = "sk_test_123",
                ["Billing:WebhookSigningSecret"] = "whsec_123",
                ["Billing:ProMonthlyPriceId"] = "price_mo",
                ["Billing:ProYearlyPriceId"] = "price_yr",
                ["Billing:CheckoutSuccessUrl"] = "https://example.com/ok",
                ["Billing:CheckoutCancelUrl"] = "https://example.com/cancel",
                ["Billing:PortalReturnUrl"] = "https://example.com/portal",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRelioBilling(configuration);
        var provider = services.BuildServiceProvider();

        var billing = provider.GetRequiredService<IBillingProvider>();
        billing.Should().BeOfType<StripeBillingProvider>();
        billing.IsBillingEnabled.Should().BeTrue();
    }
}
