using AwesomeAssertions;
using Relio.Application.Billing;
using Xunit;

namespace Relio.Application.Tests.Billing;

public sealed class BillingOptionsTests
{
    [Fact]
    public void Default_provider_is_none_and_billing_is_off()
    {
        var options = new BillingOptions();

        options.Provider.Should().Be(BillingProviderType.None);
        options.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Validate_does_not_throw_when_provider_is_none()
    {
        var options = new BillingOptions { Provider = BillingProviderType.None };

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_names_every_missing_setting_when_provider_is_stripe()
    {
        var options = new BillingOptions { Provider = BillingProviderType.Stripe };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Billing:ApiKey*")
            .WithMessage("*Billing:WebhookSigningSecret*")
            .WithMessage("*Billing:ProMonthlyPriceId*")
            .WithMessage("*Billing:ProYearlyPriceId*")
            .WithMessage("*Billing:CheckoutSuccessUrl*")
            .WithMessage("*Billing:CheckoutCancelUrl*")
            .WithMessage("*Billing:PortalReturnUrl*");
    }

    [Fact]
    public void Validate_never_quotes_a_secret_value()
    {
        var options = CompleteStripeOptions();
        options.CheckoutSuccessUrl = "not a url";

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().NotContain(options.ApiKey!).And.NotContain(options.WebhookSigningSecret!);
    }

    [Fact]
    public void Validate_succeeds_when_provider_is_stripe_and_all_settings_are_present()
    {
        var options = CompleteStripeOptions();

        var act = () => options.Validate();

        act.Should().NotThrow();
        options.IsEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://www.relio.club/Account/Manage/Plan")]
    [InlineData("/Account/Manage/Plan")]
    [InlineData("ftp://example.com/plan")]
    public void Validate_rejects_a_return_url_that_is_not_absolute_https(string url)
    {
        var options = CompleteStripeOptions();
        options.PortalReturnUrl = url;

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Billing:PortalReturnUrl*");
    }

    [Fact]
    public void Validate_allows_plain_http_on_localhost_for_development()
    {
        var options = CompleteStripeOptions();
        options.CheckoutSuccessUrl = "http://localhost:5000/Account/Manage/Plan";
        options.CheckoutCancelUrl = "http://localhost:5000/Account/Manage/Plan";
        options.PortalReturnUrl = "http://localhost:5000/Account/Manage/Plan";

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    private static BillingOptions CompleteStripeOptions() => new()
    {
        Provider = BillingProviderType.Stripe,
        ApiKey = "sk_test_fake_value_for_tests",
        WebhookSigningSecret = "whsec_fake_value_for_tests",
        ProMonthlyPriceId = "price_monthly",
        ProYearlyPriceId = "price_yearly",
        CheckoutSuccessUrl = "https://example.com/Account/Manage/Plan",
        CheckoutCancelUrl = "https://example.com/Account/Manage/Plan",
        PortalReturnUrl = "https://example.com/Account/Manage/Plan",
    };
}
