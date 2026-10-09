using Relio.Application.Billing;

namespace Relio.Data.Tests.Billing;

/// <summary>
/// An enabled <see cref="IBillingProvider"/> that never calls a payment provider: every answer is
/// set up by the test, and every call is recorded so tests can assert what would have reached Stripe.
/// </summary>
internal sealed class FakeBillingProvider : IBillingProvider
{
    public bool IsEnabled { get; set; } = true;

    public bool SignatureIsValid { get; set; } = true;

    public ParsedBillingEvent? WebhookEvent { get; set; }

    public ParsedBillingEvent? CompletedCheckout { get; set; }

    public CheckoutSessionResult CheckoutResult { get; set; } = new(true, "https://checkout.stripe.com/c/pay/cs_test_fake", null);

    public PortalSessionResult PortalResult { get; set; } = new(true, "https://billing.stripe.com/p/session/test_fake", null);

    public SubscriptionCancellationResult CancellationResult { get; set; } = SubscriptionCancellationResult.Success;

    public bool EmailUpdateSucceeds { get; set; } = true;

    public List<BillingCheckoutRequest> CheckoutRequests { get; } = [];

    public List<(string UserId, string SessionId)> CheckoutLookups { get; } = [];

    public List<string> PortalCustomers { get; } = [];

    public List<string> CancelledCustomers { get; } = [];

    public List<(string CustomerId, string Email)> EmailUpdates { get; } = [];

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        BillingCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        CheckoutRequests.Add(request);
        return Task.FromResult(CheckoutResult);
    }

    public Task<ParsedBillingEvent?> GetCompletedCheckoutAsync(
        string userId,
        string checkoutSessionId,
        CancellationToken cancellationToken = default)
    {
        CheckoutLookups.Add((userId, checkoutSessionId));
        return Task.FromResult(CompletedCheckout);
    }

    public Task<PortalSessionResult> CreatePortalSessionAsync(string customerId, CancellationToken cancellationToken = default)
    {
        PortalCustomers.Add(customerId);
        return Task.FromResult(PortalResult);
    }

    public Task<bool> UpdateCustomerEmailAsync(string customerId, string email, CancellationToken cancellationToken = default)
    {
        EmailUpdates.Add((customerId, email));
        return Task.FromResult(EmailUpdateSucceeds);
    }

    public Task<SubscriptionCancellationResult> CancelSubscriptionsAsync(string customerId, CancellationToken cancellationToken = default)
    {
        CancelledCustomers.Add(customerId);
        return Task.FromResult(CancellationResult);
    }

    public Task<WebhookVerificationResult> VerifyWebhookSignatureAsync(
        string payload,
        string signatureHeader,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WebhookVerificationResult(SignatureIsValid, SignatureIsValid ? null : "bad signature"));

    public Task<ParsedBillingEvent?> ParseWebhookEventAsync(string payload, CancellationToken cancellationToken = default) =>
        Task.FromResult(WebhookEvent);
}
