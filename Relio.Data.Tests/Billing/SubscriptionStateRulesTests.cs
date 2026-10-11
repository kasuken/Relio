using Relio.Application.Billing;
using Relio.Data.Billing;
using static Relio.Data.Tests.Billing.BillingTestHarness;

namespace Relio.Data.Tests.Billing;

public sealed class SubscriptionStateRulesTests
{
    [Fact]
    public void A_pro_event_records_the_references_tier_and_renewal()
    {
        var subscription = new UserSubscription { UserId = "u" };

        var applied = SubscriptionStateRules.Apply(subscription, ProEvent("evt_1", "u"));

        applied.Should().BeTrue();
        subscription.Tier.Should().Be(PlanTier.Pro);
        subscription.PlanRenewsAtUtc.Should().Be(Now.UtcDateTime.AddYears(1));
        subscription.BillingProviderCustomerId.Should().Be("cus_test");
        subscription.BillingProviderSubscriptionId.Should().Be("sub_test");
        subscription.LastBillingEventAtUtc.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public void An_older_event_than_the_last_applied_changes_no_state_but_keeps_references()
    {
        var subscription = new UserSubscription
        {
            UserId = "u",
            Tier = PlanTier.Free,
            LastBillingEventAtUtc = Now.UtcDateTime,
        };

        var applied = SubscriptionStateRules.Apply(
            subscription,
            ProEvent("evt_old", "u", occurredAtUtc: Now.UtcDateTime.AddMinutes(-1), customerId: "cus_new"));

        applied.Should().BeFalse();
        subscription.Tier.Should().Be(PlanTier.Free);
        subscription.BillingProviderCustomerId.Should().Be("cus_new");
    }

    [Fact]
    public void An_event_at_the_same_instant_is_not_stale_so_a_retry_still_applies()
    {
        var subscription = new UserSubscription { UserId = "u", LastBillingEventAtUtc = Now.UtcDateTime };

        SubscriptionStateRules.Apply(subscription, ProEvent("evt_retry", "u", occurredAtUtc: Now.UtcDateTime))
            .Should().BeTrue();
        subscription.Tier.Should().Be(PlanTier.Pro);
    }

    [Fact]
    public void Moving_to_free_clears_renewal_and_cancellation()
    {
        var subscription = new UserSubscription
        {
            UserId = "u",
            Tier = PlanTier.Pro,
            PlanRenewsAtUtc = Now.UtcDateTime.AddDays(10),
            PlanCancelsAtUtc = Now.UtcDateTime.AddDays(10),
            GracePeriodEndsAtUtc = Now.UtcDateTime.AddDays(2),
        };

        SubscriptionStateRules.Apply(subscription, new ParsedBillingEvent(
            "evt_deleted", "customer.subscription.deleted", "u", PlanTier.Free, null, ClearsGracePeriod: true));

        subscription.Tier.Should().Be(PlanTier.Free);
        subscription.PlanRenewsAtUtc.Should().BeNull();
        subscription.PlanCancelsAtUtc.Should().BeNull();
        subscription.GracePeriodEndsAtUtc.Should().BeNull();
    }

    [Fact]
    public void A_failed_payment_sets_a_grace_period_and_a_paid_invoice_clears_it()
    {
        var subscription = new UserSubscription { UserId = "u", Tier = PlanTier.Pro };
        var graceEnds = Now.UtcDateTime.AddDays(4);

        SubscriptionStateRules.Apply(subscription, new ParsedBillingEvent(
            "evt_failed", "invoice.payment_failed", "u", null, null, GracePeriodEndsAtUtc: graceEnds, OccurredAtUtc: Now.UtcDateTime));
        subscription.GracePeriodEndsAtUtc.Should().Be(graceEnds);
        subscription.Tier.Should().Be(PlanTier.Pro);

        SubscriptionStateRules.Apply(subscription, new ParsedBillingEvent(
            "evt_paid", "invoice.paid", "u", null, null, ClearsGracePeriod: true, OccurredAtUtc: Now.UtcDateTime.AddMinutes(1)));
        subscription.GracePeriodEndsAtUtc.Should().BeNull();
    }

    [Fact]
    public void A_cancellation_schedule_is_stored_and_withdrawn()
    {
        var subscription = new UserSubscription { UserId = "u" };
        var cancelsAt = Now.UtcDateTime.AddDays(20);

        SubscriptionStateRules.Apply(subscription, ProEvent("evt_1", "u") with { CancelsAtUtc = cancelsAt });
        subscription.PlanCancelsAtUtc.Should().Be(cancelsAt);

        SubscriptionStateRules.Apply(subscription, ProEvent("evt_2", "u", occurredAtUtc: Now.UtcDateTime.AddMinutes(1)));
        subscription.PlanCancelsAtUtc.Should().BeNull();
    }

    [Fact]
    public void A_link_only_event_records_references_without_touching_the_tier_or_event_time()
    {
        var subscription = new UserSubscription { UserId = "u" };

        SubscriptionStateRules.Apply(subscription, new ParsedBillingEvent(
            "evt_checkout", "checkout.session.completed", "u", null, null,
            BillingProviderCustomerId: "cus_1", BillingProviderSubscriptionId: "sub_1", OccurredAtUtc: Now.UtcDateTime))
            .Should().BeTrue();

        subscription.Tier.Should().Be(PlanTier.Free);
        subscription.BillingProviderCustomerId.Should().Be("cus_1");
        subscription.LastBillingEventAtUtc.Should().BeNull();
    }

    [Fact]
    public void Pro_is_suspended_only_once_its_grace_period_has_passed()
    {
        var now = Now.UtcDateTime;

        SubscriptionStateRules.EffectiveTier(PlanTier.Pro, null, now).Should().Be(PlanTier.Pro);
        SubscriptionStateRules.EffectiveTier(PlanTier.Pro, now.AddSeconds(1), now).Should().Be(PlanTier.Pro);
        SubscriptionStateRules.EffectiveTier(PlanTier.Pro, now, now).Should().Be(PlanTier.Free);
        SubscriptionStateRules.IsPaidAccessSuspended(PlanTier.Free, now.AddDays(-1), now).Should().BeFalse();
    }
}
