namespace Relio.Application.Billing;

/// <summary>
/// Safe no-op <see cref="IBillingProvider"/> used when <c>Billing:Provider</c> is
/// <see cref="BillingProviderType.None"/> (the default). Checkout and the portal report themselves
/// unavailable, webhooks always fail verification (so no request can change anyone's plan), and there
/// is nothing to cancel. Plans and limits do not apply at all on such an instance.
/// </summary>
public sealed class NullBillingProvider : IBillingProvider
{
    /// <summary>The reason shown when someone reaches a billing action on an instance without billing.</summary>
    public const string NotConfiguredReason =
        "This Relio instance has no paid plans. Everything is already included.";

    public bool IsEnabled => false;

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        BillingCheckoutRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(CheckoutSessionResult.Unsupported(NotConfiguredReason));

    public Task<ParsedBillingEvent?> GetCompletedCheckoutAsync(
        string userId,
        string checkoutSessionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ParsedBillingEvent?>(null);

    public Task<PortalSessionResult> CreatePortalSessionAsync(
        string customerId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PortalSessionResult.Unsupported(NotConfiguredReason));

    public Task<bool> UpdateCustomerEmailAsync(
        string customerId,
        string email,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<SubscriptionCancellationResult> CancelSubscriptionsAsync(
        string customerId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SubscriptionCancellationResult.Success);

    public Task<WebhookVerificationResult> VerifyWebhookSignatureAsync(
        string payload,
        string signatureHeader,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WebhookVerificationResult(false, "No payment provider is configured; webhooks are not accepted."));

    public Task<ParsedBillingEvent?> ParseWebhookEventAsync(
        string payload,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ParsedBillingEvent?>(null);
}
