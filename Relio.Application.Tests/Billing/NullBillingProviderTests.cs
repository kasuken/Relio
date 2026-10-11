using AwesomeAssertions;
using Relio.Application.Billing;
using Xunit;

namespace Relio.Application.Tests.Billing;

public sealed class NullBillingProviderTests
{
    private readonly NullBillingProvider _provider = new();

    [Fact]
    public void Billing_is_off()
    {
        _provider.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Checkout_and_portal_are_unsupported_with_a_calm_reason()
    {
        var checkout = await _provider.CreateCheckoutSessionAsync(
            new BillingCheckoutRequest("user", BillingInterval.Yearly, null, null));
        var portal = await _provider.CreatePortalSessionAsync("cus_1");

        checkout.Supported.Should().BeFalse();
        checkout.RedirectUrl.Should().BeNull();
        checkout.UnsupportedReason.Should().Be(NullBillingProvider.NotConfiguredReason);
        portal.Supported.Should().BeFalse();
        portal.RedirectUrl.Should().BeNull();
    }

    [Fact]
    public async Task Webhooks_never_verify_so_no_request_can_change_a_plan()
    {
        var verification = await _provider.VerifyWebhookSignatureAsync("{}", "t=1,v1=abc");
        var parsed = await _provider.ParseWebhookEventAsync("{}");

        verification.IsValid.Should().BeFalse();
        parsed.Should().BeNull();
    }

    [Fact]
    public async Task There_is_nothing_to_cancel_or_sync()
    {
        (await _provider.CancelSubscriptionsAsync("cus_1")).Succeeded.Should().BeTrue();
        (await _provider.UpdateCustomerEmailAsync("cus_1", "a@example.com")).Should().BeTrue();
        (await _provider.GetCompletedCheckoutAsync("user", "cs_test_1")).Should().BeNull();
    }
}
