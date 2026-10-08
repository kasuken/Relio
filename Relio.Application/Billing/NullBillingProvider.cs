namespace Relio.Application.Billing;

/// <summary>
/// Safe no-op <see cref="IBillingProvider"/> used when <c>Billing:Provider</c> is
/// <see cref="BillingProviderType.None"/> (the default). Checkout and portal requests
/// report themselves as unsupported; webhooks always fail verification.
/// All features run unconstrained without payment gating.
/// </summary>
public sealed class NullBillingProvider : IBillingProvider
{
    private const string NotConfiguredReason =
        "No payment provider is configured. Self-hosted and free instances include full functionality.";

    public bool IsBillingEnabled => false;

    public BillingProviderType ProviderType => BillingProviderType.None;

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        string userId,
        string priceId,
        string? accountEmail = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new CheckoutSessionResult(false, null, NotConfiguredReason));

    public Task<PortalSessionResult> CreatePortalSessionAsync(
        string customerId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PortalSessionResult(false, null, NotConfiguredReason));

    public Task<WebhookVerificationResult> VerifyWebhookSignatureAsync(
        string payload,
        string signatureHeader,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WebhookVerificationResult(false, "No payment provider is configured; webhooks are not accepted."));
}
