using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Relio.Application.Billing;
using Relio.Web.Billing;

namespace Relio.Web.Tests.Billing;

public sealed class BillingDependencyInjectionTests
{
    // Obviously fake values: no real key or secret is ever used in tests.
    private static Dictionary<string, string?> StripeSettings() => new()
    {
        ["Billing:Provider"] = "Stripe",
        ["Billing:ApiKey"] = "sk_test_fake_key_never_sent_anywhere",
        ["Billing:WebhookSigningSecret"] = "whsec_test_fake_secret",
        ["Billing:ProMonthlyPriceId"] = "price_mo",
        ["Billing:ProYearlyPriceId"] = "price_yr",
        ["Billing:CheckoutSuccessUrl"] = "https://example.com/ok",
        ["Billing:CheckoutCancelUrl"] = "https://example.com/cancel",
        ["Billing:PortalReturnUrl"] = "https://example.com/portal",
    };

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static ServiceCollection ServicesWithStripePrerequisites()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        return services;
    }

    [Fact]
    public void AddRelioBilling_defaults_to_NullBillingProvider_when_provider_is_none()
    {
        var services = new ServiceCollection();
        services.AddRelioBilling(Configuration(new() { ["Billing:Provider"] = "None" }));
        using var provider = services.BuildServiceProvider();

        var billing = provider.GetRequiredService<IBillingProvider>();
        billing.Should().BeOfType<NullBillingProvider>();
        billing.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void AddRelioBilling_defaults_to_NullBillingProvider_when_the_section_is_missing()
    {
        var services = new ServiceCollection();
        services.AddRelioBilling(Configuration(new()));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBillingProvider>().Should().BeOfType<NullBillingProvider>();
    }

    [Fact]
    public void AddRelioBilling_registers_StripeBillingProvider_when_provider_is_stripe()
    {
        var services = ServicesWithStripePrerequisites();
        services.AddRelioBilling(Configuration(StripeSettings()));
        using var provider = services.BuildServiceProvider();

        var billing = provider.GetRequiredService<IBillingProvider>();
        billing.Should().BeOfType<StripeBillingProvider>();
        billing.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void AddRelioBilling_allows_plain_http_urls_on_localhost()
    {
        var settings = StripeSettings();
        settings["Billing:CheckoutSuccessUrl"] = "http://localhost:5000/Account/Manage/Plan";
        settings["Billing:CheckoutCancelUrl"] = "http://127.0.0.1:5000/Account/Manage/Plan";
        settings["Billing:PortalReturnUrl"] = "http://localhost/Account/Manage/Plan";
        var services = ServicesWithStripePrerequisites();

        services.AddRelioBilling(Configuration(settings));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBillingProvider>().Should().BeOfType<StripeBillingProvider>();
    }

    [Fact]
    public void AddRelioBilling_with_stripe_and_missing_settings_throws_naming_every_missing_key()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Billing:Provider"] = "Stripe",
            ["Billing:ApiKey"] = "sk_test_fake_key_never_sent_anywhere",
            ["Billing:ProMonthlyPriceId"] = "price_mo",
            ["Billing:CheckoutCancelUrl"] = "https://example.com/cancel",
        };

        var act = () => ServicesWithStripePrerequisites().AddRelioBilling(Configuration(settings));

        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().ContainAll(
            "Billing:WebhookSigningSecret",
            "Billing:ProYearlyPriceId",
            "Billing:CheckoutSuccessUrl",
            "Billing:PortalReturnUrl");
        exception.Message.Should().NotContainAny("Billing:ApiKey", "Billing:ProMonthlyPriceId", "Billing:CheckoutCancelUrl");
        // Settings only, never their values.
        exception.Message.Should().NotContain("sk_test_fake_key_never_sent_anywhere");
    }

    [Theory]
    [InlineData("Billing:CheckoutSuccessUrl", "/Account/Manage/Plan")]
    [InlineData("Billing:CheckoutCancelUrl", "http://example.com/cancel")]
    [InlineData("Billing:PortalReturnUrl", "ftp://example.com/portal")]
    [InlineData("Billing:PortalReturnUrl", "not a url")]
    public void AddRelioBilling_with_stripe_rejects_return_urls_that_are_not_absolute_https(string key, string value)
    {
        var settings = StripeSettings();
        settings[key] = value;

        var act = () => ServicesWithStripePrerequisites().AddRelioBilling(Configuration(settings));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{key}*");
    }
}
