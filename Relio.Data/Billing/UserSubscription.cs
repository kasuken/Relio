using Relio.Application.Billing;

namespace Relio.Data.Billing;

/// <summary>
/// A user's hosted plan and the billing provider's references for it. One row per user, created the
/// first time a billing event or a checkout concerns them; no row means the free plan.
/// </summary>
/// <remarks>
/// <para>
/// Billing infrastructure next to <c>RelioUser</c>, like <c>RegistrationInvitation</c>: it holds no
/// relationship content, so it is deliberately not an <c>IOwnedEntity</c> and <c>Relio.Domain</c>
/// knows nothing about it. It is not part of the data export (the provider keeps the billing history,
/// reachable through its portal) and a restore never creates one, so a restore can never grant a plan.
/// Account erasure deletes it after cancelling the subscription at the provider.
/// </para>
/// <para>
/// Only the webhook processor and the checkout confirmation write it, through
/// <see cref="SubscriptionStateRules.Apply"/>.
/// </para>
/// </remarks>
public sealed class UserSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The Relio user (an <c>AspNetUsers</c> id).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>The tier the provider last reported.</summary>
    public PlanTier Tier { get; set; } = PlanTier.Free;

    /// <summary>When the current paid period renews.</summary>
    public DateTime? PlanRenewsAtUtc { get; set; }

    /// <summary>When a cancelled subscription stops granting paid access; while set, it does not renew.</summary>
    public DateTime? PlanCancelsAtUtc { get; set; }

    /// <summary>
    /// After a failed payment, until when paid access continues. Once it has passed, the free plan's
    /// limit applies again even though <see cref="Tier"/> still reads Pro, until a payment succeeds or
    /// the provider ends the subscription.
    /// </summary>
    public DateTime? GracePeriodEndsAtUtc { get; set; }

    /// <summary>Creation time of the newest applied event; older events change no state.</summary>
    public DateTime? LastBillingEventAtUtc { get; set; }

    /// <summary>The provider's customer id (Stripe <c>cus_...</c>).</summary>
    public string? BillingProviderCustomerId { get; set; }

    /// <summary>The provider's subscription id (Stripe <c>sub_...</c>).</summary>
    public string? BillingProviderSubscriptionId { get; set; }
}
