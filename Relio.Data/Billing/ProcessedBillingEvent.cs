namespace Relio.Data.Billing;

/// <summary>
/// Idempotency ledger for billing webhooks: a provider event id that has been applied. Stripe
/// delivers at least once, so a retried delivery finds its id here and changes nothing.
/// </summary>
/// <remarks>
/// Deliberately minimal: no user id, no payload. It records that an event was handled, not who it
/// concerned, so it holds no personal data and needs no erasure step.
/// </remarks>
public sealed class ProcessedBillingEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The provider's unique event id (Stripe <c>evt_...</c>).</summary>
    public string ProviderEventId { get; set; } = string.Empty;

    /// <summary>The provider's event type, e.g. <c>customer.subscription.updated</c>.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>When the event was applied (UTC).</summary>
    public DateTime ProcessedAtUtc { get; set; }
}
