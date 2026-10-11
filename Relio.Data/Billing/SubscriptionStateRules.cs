using Relio.Application.Billing;

namespace Relio.Data.Billing;

/// <summary>
/// The one place a billing event changes a <see cref="UserSubscription"/>, shared by the webhook
/// processor and the checkout confirmation so both apply identical rules. Pure: no database, no clock.
/// </summary>
public static class SubscriptionStateRules
{
    /// <summary>
    /// Applies <paramref name="billingEvent"/> to <paramref name="subscription"/>. Returns false when the
    /// event is older than the newest one already applied, in which case only the provider references
    /// were recorded (they only ever fill in ids, so they are safe whatever the event's age).
    /// </summary>
    public static bool Apply(UserSubscription subscription, ParsedBillingEvent billingEvent)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(billingEvent);

        if (billingEvent.BillingProviderCustomerId is { } customerId)
        {
            subscription.BillingProviderCustomerId = customerId;
        }

        if (billingEvent.BillingProviderSubscriptionId is { } subscriptionId)
        {
            subscription.BillingProviderSubscriptionId = subscriptionId;
        }

        var changesState = billingEvent.NewTier.HasValue
            || billingEvent.ClearsGracePeriod
            || billingEvent.GracePeriodEndsAtUtc.HasValue
            || billingEvent.HasCancellationSchedule;
        if (!changesState)
        {
            return true;
        }

        // Providers deliver out of order: a subscription.updated (active) arriving after the
        // subscription.deleted that followed it must not put the user back on Pro. An event created at
        // the same instant is not stale, so a retried delivery still goes through.
        if (billingEvent.OccurredAtUtc is { } occurredAtUtc)
        {
            if (subscription.LastBillingEventAtUtc > occurredAtUtc)
            {
                return false;
            }

            subscription.LastBillingEventAtUtc = occurredAtUtc;
        }

        if (billingEvent.ClearsGracePeriod)
        {
            subscription.GracePeriodEndsAtUtc = null;
        }
        else if (billingEvent.GracePeriodEndsAtUtc is { } gracePeriodEndsAtUtc)
        {
            subscription.GracePeriodEndsAtUtc = gracePeriodEndsAtUtc;
        }

        // Before the tier, because moving to Free clears the schedule again.
        if (billingEvent.HasCancellationSchedule)
        {
            subscription.PlanCancelsAtUtc = billingEvent.CancelsAtUtc;
        }

        if (billingEvent.NewTier is { } tier)
        {
            subscription.Tier = tier;
            if (tier == PlanTier.Free)
            {
                // Nothing renews and nothing is left to cancel.
                subscription.PlanRenewsAtUtc = null;
                subscription.PlanCancelsAtUtc = null;
            }
            else
            {
                subscription.PlanRenewsAtUtc = billingEvent.PeriodEndsAtUtc ?? subscription.PlanRenewsAtUtc;
            }
        }

        return true;
    }

    /// <summary>
    /// A paid tier whose payment-failure grace period has passed. Evaluated on read rather than by a
    /// background job, so it takes effect the moment the grace period ends and reverses itself as soon
    /// as a successful charge clears it.
    /// </summary>
    public static bool IsPaidAccessSuspended(PlanTier tier, DateTime? gracePeriodEndsAtUtc, DateTime nowUtc) =>
        tier != PlanTier.Free && gracePeriodEndsAtUtc <= nowUtc;

    /// <summary>The tier enforced right now.</summary>
    public static PlanTier EffectiveTier(PlanTier tier, DateTime? gracePeriodEndsAtUtc, DateTime nowUtc) =>
        IsPaidAccessSuspended(tier, gracePeriodEndsAtUtc, nowUtc) ? PlanTier.Free : tier;
}
