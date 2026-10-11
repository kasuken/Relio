namespace Relio.Application.Billing;

/// <summary>
/// Provider-agnostic payment abstraction. <c>Billing:Provider</c> selects the implementation:
/// <see cref="NullBillingProvider"/> is the safe default and <c>Relio.Web.Billing.StripeBillingProvider</c>
/// the live one. Implementations only talk to the provider; they never read or write Relio's
/// database (the data services in <c>Relio.Data.Billing</c> do that), so the plan state stays in one
/// place and a provider can be a singleton.
/// </summary>
/// <remarks>
/// Only opaque ids and the account email are ever sent to the provider - never a person, note or any
/// other relationship content. Implementations never log an email address.
/// </remarks>
public interface IBillingProvider
{
    /// <summary>True when a live provider is configured. False under <see cref="NullBillingProvider"/>.</summary>
    bool IsEnabled { get; }

    /// <summary>Starts a hosted checkout for Relio Pro.</summary>
    Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        BillingCheckoutRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a checkout the user just returned from, so the plan applies without waiting for its
    /// webhook. Null unless it is a completed subscription checkout that was started by <paramref name="userId"/>.
    /// </summary>
    Task<ParsedBillingEvent?> GetCompletedCheckoutAsync(
        string userId,
        string checkoutSessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Opens the hosted self-service portal for a billing customer.</summary>
    Task<PortalSessionResult> CreatePortalSessionAsync(
        string customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Updates the billing customer's email, so receipts follow a changed account email.</summary>
    /// <returns>False when the provider rejected the update.</returns>
    Task<bool> UpdateCustomerEmailAsync(
        string customerId,
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Immediately cancels every live Relio subscription of a customer, so deleting an account never
    /// leaves the provider charging for it. Another product's subscription on the same customer is left alone.
    /// </summary>
    Task<SubscriptionCancellationResult> CancelSubscriptionsAsync(
        string customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Verifies a webhook request's signature before its payload is trusted.</summary>
    Task<WebhookVerificationResult> VerifyWebhookSignatureAsync(
        string payload,
        string signatureHeader,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses a signature-verified webhook payload. Null for event types Relio does not act on and for
    /// events that belong to another product on a shared provider account.
    /// </summary>
    Task<ParsedBillingEvent?> ParseWebhookEventAsync(
        string payload,
        CancellationToken cancellationToken = default);
}
