using AwesomeAssertions;
using Relio.Application.Billing;
using Xunit;

namespace Relio.Application.Tests.Billing;

public sealed class NullBillingProviderTests
{
    [Fact]
    public async Task NullBillingProvider_has_billing_disabled_and_returns_unsupported_results()
    {
        var provider = new NullBillingProvider();

        provider.IsBillingEnabled.Should().BeFalse();
        provider.ProviderType.Should().Be(BillingProviderType.None);

        var checkout = await provider.CreateCheckoutSessionAsync("user1", "price1");
        checkout.Success.Should().BeFalse();
        checkout.Url.Should().BeNull();
        checkout.ErrorMessage.Should().Contain("No payment provider is configured");

        var portal = await provider.CreatePortalSessionAsync("cust1");
        portal.Success.Should().BeFalse();
        portal.Url.Should().BeNull();

        var webhook = await provider.VerifyWebhookSignatureAsync("payload", "sig");
        webhook.Success.Should().BeFalse();
    }
}
