namespace Relio.Application.Billing;

/// <summary>Result of asking the billing provider to start a hosted checkout.</summary>
/// <param name="Supported">False when checkout cannot start; <paramref name="UnsupportedReason"/> says why.</param>
/// <param name="RedirectUrl">The provider-hosted checkout URL, when supported.</param>
/// <param name="UnsupportedReason">A calm, user-facing explanation when unsupported.</param>
public sealed record CheckoutSessionResult(bool Supported, string? RedirectUrl, string? UnsupportedReason)
{
    public static CheckoutSessionResult Unsupported(string reason) => new(false, null, reason);
}

/// <summary>Result of asking the billing provider to open its self-service portal.</summary>
public sealed record PortalSessionResult(bool Supported, string? RedirectUrl, string? UnsupportedReason)
{
    public static PortalSessionResult Unsupported(string reason) => new(false, null, reason);
}

/// <summary>Result of verifying an inbound webhook's signature.</summary>
public sealed record WebhookVerificationResult(bool IsValid, string? FailureReason);

/// <summary>Result of cancelling every live Relio subscription of a billing customer.</summary>
/// <param name="Succeeded">True when nothing is left billing the customer, including when there was nothing to cancel.</param>
/// <param name="FailureReason">A log-friendly reason when cancellation failed; never contains personal data.</param>
public sealed record SubscriptionCancellationResult(bool Succeeded, string? FailureReason = null)
{
    public static readonly SubscriptionCancellationResult Success = new(true);
}

/// <summary>What a checkout needs to know about the user who starts it.</summary>
/// <param name="UserId">The Relio user id, stamped on the checkout and the subscription it creates.</param>
/// <param name="Interval">Monthly or yearly Relio Pro.</param>
/// <param name="ExistingCustomerId">The user's billing customer from an earlier subscription, if any.</param>
/// <param name="AccountEmail">
/// The account's email, so a first checkout creates the customer under the address the user signs in
/// with rather than one the hosted page collects. Ignored once a customer exists.
/// </param>
public sealed record BillingCheckoutRequest(
    string UserId,
    BillingInterval Interval,
    string? ExistingCustomerId,
    string? AccountEmail);

/// <summary>
/// A billing event (a webhook, or a completed checkout read when the user returns), reduced to the
/// fields Relio's plan state needs.
/// </summary>
/// <param name="ProviderEventId">The provider's unique event id, the idempotency key.</param>
/// <param name="EventType">The provider's event type, kept for the ledger.</param>
/// <param name="TargetUserId">The Relio user named by the provider's own metadata, when present.</param>
/// <param name="NewTier">The tier to apply, when the event is a plan change.</param>
/// <param name="PeriodEndsAtUtc">The next renewal, when the event carries one.</param>
/// <param name="BillingProviderCustomerId">The customer id to remember; null leaves the stored one.</param>
/// <param name="BillingProviderSubscriptionId">The subscription id to remember; null leaves the stored one.</param>
/// <param name="GracePeriodEndsAtUtc">After a failed payment, until when paid access continues.</param>
/// <param name="ClearsGracePeriod">True when a successful charge (or an active state) ends any grace period.</param>
/// <param name="OccurredAtUtc">
/// When the provider created the event; an older event than the last one applied changes no state,
/// because providers do not deliver in order. Null means apply regardless of order.
/// </param>
/// <param name="HasCancellationSchedule">True when <paramref name="CancelsAtUtc"/> is to be stored even when null.</param>
/// <param name="CancelsAtUtc">When a cancelled subscription stops granting paid access.</param>
/// <param name="MayResolveByCustomer">
/// True when the event is known to be a Relio subscription's, so a missing
/// <paramref name="TargetUserId"/> may be resolved from the stored customer id. An invoice that bills
/// no Relio price leaves this false: a shared Stripe account's other products bill the same customer.
/// </param>
public sealed record ParsedBillingEvent(
    string ProviderEventId,
    string EventType,
    string? TargetUserId,
    PlanTier? NewTier,
    DateTime? PeriodEndsAtUtc,
    string? BillingProviderCustomerId = null,
    string? BillingProviderSubscriptionId = null,
    DateTime? GracePeriodEndsAtUtc = null,
    bool ClearsGracePeriod = false,
    DateTime? OccurredAtUtc = null,
    bool HasCancellationSchedule = false,
    DateTime? CancelsAtUtc = null,
    bool MayResolveByCustomer = false);

/// <summary>Outcome of processing one inbound billing webhook delivery.</summary>
/// <param name="Accepted">
/// True when the request is acknowledged (HTTP 2xx): applied, a duplicate, stale, irrelevant or for
/// an unknown user. False only when the signature check failed.
/// </param>
/// <param name="Reason">A short machine-readable outcome code, safe to log.</param>
public sealed record BillingWebhookProcessingResult(bool Accepted, string Reason)
{
    public static readonly BillingWebhookProcessingResult InvalidSignature = new(false, "invalid_signature");
    public static readonly BillingWebhookProcessingResult Ignored = new(true, "ignored_event_type");
    public static readonly BillingWebhookProcessingResult AlreadyProcessed = new(true, "already_processed");
    public static readonly BillingWebhookProcessingResult Applied = new(true, "applied");
    public static readonly BillingWebhookProcessingResult StaleEvent = new(true, "stale_event");
    public static readonly BillingWebhookProcessingResult UnknownUser = new(true, "unknown_user");
}

/// <summary>Everything the plan page shows about the signed-in user's plan.</summary>
/// <param name="BillingEnabled">False on an instance without a billing provider: no plans, no limits.</param>
/// <param name="Tier">The tier enforced right now, which is Free while <paramref name="PaidAccessSuspended"/>.</param>
/// <param name="ActivePeople">How many people are active (not archived).</param>
/// <param name="MaxActivePeople">The enforced limit; null means unlimited.</param>
/// <param name="PlanRenewsAtUtc">When a paid plan renews.</param>
/// <param name="PlanCancelsAtUtc">When a cancelled paid plan ends; while set it does not renew.</param>
/// <param name="GracePeriodEndsAtUtc">After a failed payment, until when Pro continues.</param>
/// <param name="PaidAccessSuspended">
/// True when the provider still reports a paid subscription but its grace period has passed, so Pro is
/// withheld until a payment succeeds or the subscription ends.
/// </param>
/// <param name="HasBillingAccount">True when there is a billing customer, so the portal can open.</param>
public sealed record PlanSummary(
    bool BillingEnabled,
    PlanTier Tier,
    int ActivePeople,
    int? MaxActivePeople,
    DateTime? PlanRenewsAtUtc,
    DateTime? PlanCancelsAtUtc,
    DateTime? GracePeriodEndsAtUtc,
    bool PaidAccessSuspended,
    bool HasBillingAccount)
{
    /// <summary>The plan definition for <see cref="Tier"/>.</summary>
    public PlanDefinition Plan => PlanCatalog.Get(Tier);

    /// <summary>True when no more people can be added or restored.</summary>
    public bool LimitReached => MaxActivePeople is int max && ActivePeople >= max;
}
