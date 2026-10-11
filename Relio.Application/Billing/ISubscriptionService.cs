namespace Relio.Application.Billing;

/// <summary>
/// The signed-in user's hosted plan: what it is, how to subscribe, and the billing portal. Every
/// method acts on the current user only (<c>ICurrentUser</c>) and takes no user id.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>The current user's plan and usage.</summary>
    Task<PlanSummary> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a hosted checkout for Relio Pro. Unsupported (with a reason) when billing is off or the
    /// user already has a live subscription, so a double submit never creates a second one.
    /// </summary>
    Task<CheckoutSessionResult> StartCheckoutAsync(
        BillingInterval interval,
        CancellationToken cancellationToken = default);

    /// <summary>Opens the hosted billing portal. Unsupported when the user has no billing account yet.</summary>
    Task<PortalSessionResult> OpenPortalAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the subscription a checkout created, read live from the provider when the user returns
    /// from it, so the upgrade shows at once instead of when the webhook lands. True when the user is
    /// now on Relio Pro; false for an unknown, incomplete or someone else's checkout.
    /// </summary>
    Task<bool> ConfirmCheckoutAsync(string checkoutSessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Verifies and idempotently applies inbound billing webhooks. Not scoped to a signed-in user: the
/// provider's signature is the capability, and the user comes from the event itself.
/// </summary>
public interface IBillingWebhookProcessor
{
    /// <summary>
    /// Verifies <paramref name="signatureHeader"/> against <paramref name="payload"/> and, when valid,
    /// applies the event exactly once per provider event id.
    /// </summary>
    Task<BillingWebhookProcessingResult> ProcessAsync(
        string payload,
        string signatureHeader,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps the billing customer's email in step with the account email. Called by the email-change
/// pages once Identity has confirmed the change for that user, which may happen in a browser that is
/// not signed in as them, so it takes the user id Identity verified.
/// </summary>
public interface IBillingCustomerEmailSync
{
    /// <summary>
    /// Best effort: true when the provider accepted the new email or the user has no billing customer.
    /// </summary>
    Task<bool> SyncAsync(string userId, string email, CancellationToken cancellationToken = default);
}
