using AwesomeAssertions;
using Relio.Application.Billing;
using Xunit;

namespace Relio.Application.Tests.Billing;

public sealed class BillingOptionsTests
{
    [Fact]
    public void Default_provider_is_none()
    {
        var options = new BillingOptions();

        options.Provider.Should().Be(BillingProviderType.None);
        options.IsStripeFullyConfigured.Should().BeFalse();
    }

    [Fact]
    public void Validate_does_not_throw_when_provider_is_none()
    {
        var options = new BillingOptions { Provider = BillingProviderType.None };

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_throws_when_provider_is_stripe_and_settings_are_missing()
    {
        var options = new BillingOptions
        {
            Provider = BillingProviderType.Stripe,
            ApiKey = null,
            WebhookSigningSecret = null,
        };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Billing:ApiKey*")
            .WithMessage("*Billing:WebhookSigningSecret*")
            .WithMessage("*Billing:ProMonthlyPriceId*")
            .WithMessage("*Billing:ProYearlyPriceId*");
    }

    [Fact]
    public void Validate_succeeds_when_provider_is_stripe_and_all_settings_present()
    {
        var options = new BillingOptions
        {
            Provider = BillingProviderType.Stripe,
            ApiKey = "sk_test_123",
            WebhookSigningSecret = "whsec_123",
            ProMonthlyPriceId = "price_monthly",
            ProYearlyPriceId = "price_yearly",
            CheckoutSuccessUrl = "https://example.com/success",
            CheckoutCancelUrl = "https://example.com/cancel",
            PortalReturnUrl = "https://example.com/settings",
        };

        var act = () => options.Validate();

        act.Should().NotThrow();
        options.IsStripeFullyConfigured.Should().BeTrue();
    }
}
