namespace Relio.Application.Billing;

/// <summary>
/// Provider-agnostic abstraction for payment and subscription services.
/// </summary>
public interface IBillingProvider
{
    /// <summary>
    /// Indicates whether billing features are currently active and available.
    /// Returns false when <c>Billing:Provider=None</c>.
    /// </summary>
    bool IsBillingEnabled { get; }

    /// <summary>
    /// Gets the provider type currently configured.
    /// </summary>
    BillingProviderType ProviderType { get; }

    /// <summary>
    /// Starts a checkout session for upgrading to a paid tier.
    /// </summary>
    Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        string userId,
        string priceId,
        string? accountEmail = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a self-service customer portal session.
    /// </summary>
    Task<PortalSessionResult> CreatePortalSessionAsync(
        string customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies an inbound webhook signature.
    /// </summary>
    Task<WebhookVerificationResult> VerifyWebhookSignatureAsync(
        string payload,
        string signatureHeader,
        CancellationToken cancellationToken = default);
}
