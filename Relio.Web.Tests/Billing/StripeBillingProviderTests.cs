using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Relio.Application.Billing;
using Relio.Web.Billing;
using Xunit;

namespace Relio.Web.Tests.Billing;

public sealed class StripeBillingProviderTests
{
    [Fact]
    public async Task Webhook_verification_succeeds_with_valid_signature()
    {
        var secret = "whsec_test_secret_key_12345";
        var options = Options.Create(new BillingOptions
        {
            Provider = BillingProviderType.Stripe,
            ApiKey = "sk_test_123",
            WebhookSigningSecret = secret,
            ProMonthlyPriceId = "price_mo",
            ProYearlyPriceId = "price_yr",
        });

        using var client = new HttpClient();
        var provider = new StripeBillingProvider(client, options, NullLogger<StripeBillingProvider>.Instance);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var payload = "{\"id\":\"evt_123\",\"type\":\"checkout.session.completed\"}";
        var signedPayload = $"{timestamp}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload))).ToLowerInvariant();
        var header = $"t={timestamp},v1={signature}";

        var result = await provider.VerifyWebhookSignatureAsync(payload, header);

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Webhook_verification_fails_with_invalid_signature()
    {
        var secret = "whsec_test_secret_key_12345";
        var options = Options.Create(new BillingOptions
        {
            Provider = BillingProviderType.Stripe,
            WebhookSigningSecret = secret,
        });

        using var client = new HttpClient();
        var provider = new StripeBillingProvider(client, options, NullLogger<StripeBillingProvider>.Instance);

        var header = "t=123456789,v1=invalid_signature_hex";
        var result = await provider.VerifyWebhookSignatureAsync("{}", header);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Signature mismatch");
    }

    [Fact]
    public async Task Checkout_session_returns_error_when_stripe_is_not_fully_configured()
    {
        var options = Options.Create(new BillingOptions
        {
            Provider = BillingProviderType.Stripe,
            ApiKey = null,
        });

        using var client = new HttpClient();
        var provider = new StripeBillingProvider(client, options, NullLogger<StripeBillingProvider>.Instance);

        var result = await provider.CreateCheckoutSessionAsync("user1", "price1");
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not fully configured");
    }
}
