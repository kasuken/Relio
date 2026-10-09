using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Billing;
using Relio.Data.Billing;
using static Relio.Data.Tests.Billing.BillingTestHarness;

namespace Relio.Data.Tests.Billing;

public sealed class BillingWebhookProcessorTests
{
    private const string UserId = "billing-user";

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeBillingProvider _provider = new();

    [Fact]
    public async Task An_invalid_signature_is_rejected_and_writes_nothing()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddUserAsync(dbContext, UserId);
        _provider.SignatureIsValid = false;
        _provider.WebhookEvent = ProEvent("evt_1", UserId);

        var result = await CreateProcessor(dbContext).ProcessAsync("{}", "t=1,v1=forged");

        result.Should().Be(BillingWebhookProcessingResult.InvalidSignature);
        (await dbContext.ProcessedBillingEvents.AnyAsync()).Should().BeFalse();
        (await ReadSubscriptionAsync(dbContext, UserId)).Should().BeNull();
    }

    [Fact]
    public async Task Without_a_billing_provider_no_webhook_is_accepted()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddUserAsync(dbContext, UserId);
        var processor = new BillingWebhookProcessor(
            dbContext, new NullBillingProvider(), _clock, NullLogger<BillingWebhookProcessor>.Instance);

        var result = await processor.ProcessAsync("{}", "t=1,v1=abc");

        result.Accepted.Should().BeFalse();
        (await ReadSubscriptionAsync(dbContext, UserId)).Should().BeNull();
    }

    [Fact]
    public async Task An_event_Relio_does_not_act_on_is_acknowledged_without_a_ledger_row()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        _provider.WebhookEvent = null;

        var result = await CreateProcessor(dbContext).ProcessAsync("{}", "sig");

        result.Should().Be(BillingWebhookProcessingResult.Ignored);
        (await dbContext.ProcessedBillingEvents.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task A_pro_event_for_the_metadata_user_upgrades_them_and_is_recorded_once()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddUserAsync(dbContext, UserId);
        _provider.WebhookEvent = ProEvent("evt_pro", UserId);

        var result = await CreateProcessor(dbContext).ProcessAsync("{}", "sig");

        result.Should().Be(BillingWebhookProcessingResult.Applied);
        var stored = await ReadSubscriptionAsync(dbContext, UserId);
        stored!.Tier.Should().Be(PlanTier.Pro);
        stored.BillingProviderCustomerId.Should().Be("cus_test");
        stored.BillingProviderSubscriptionId.Should().Be("sub_test");
        var ledger = await dbContext.ProcessedBillingEvents.AsNoTracking().SingleAsync();
        ledger.ProviderEventId.Should().Be("evt_pro");
        ledger.EventType.Should().Be("customer.subscription.updated");
        ledger.ProcessedAtUtc.Should().Be(Now.UtcDateTime);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task A_repeated_delivery_of_the_same_event_changes_nothing()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddUserAsync(dbContext, UserId);
        _provider.WebhookEvent = ProEvent("evt_pro", UserId);
        await CreateProcessor(dbContext).ProcessAsync("{}", "sig");

        // The same event id again, even with different contents, is a no-op.
        _provider.WebhookEvent = new ParsedBillingEvent(
            "evt_pro", "customer.subscription.deleted", UserId, PlanTier.Free, null, OccurredAtUtc: Now.UtcDateTime.AddDays(1));
        var result = await CreateProcessor(dbContext).ProcessAsync("{}", "sig");

        result.Should().Be(BillingWebhookProcessingResult.AlreadyProcessed);
        (await ReadSubscriptionAsync(dbContext, UserId))!.Tier.Should().Be(PlanTier.Pro);
        (await dbContext.ProcessedBillingEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task An_older_event_delivered_late_is_recorded_but_does_not_restore_pro()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddUserAsync(dbContext, UserId);
        _provider.WebhookEvent = new ParsedBillingEvent(
            "evt_deleted", "customer.subscription.deleted", UserId, PlanTier.Free, null,
            ClearsGracePeriod: true, OccurredAtUtc: Now.UtcDateTime);
        await CreateProcessor(dbContext).ProcessAsync("{}", "sig");

        _provider.WebhookEvent = ProEvent("evt_updated_earlier", UserId, occurredAtUtc: Now.UtcDateTime.AddMinutes(-5));
        var result = await CreateProcessor(dbContext).ProcessAsync("{}", "sig");

        result.Should().Be(BillingWebhookProcessingResult.StaleEvent);
        (await ReadSubscriptionAsync(dbContext, UserId))!.Tier.Should().Be(PlanTier.Free);
        (await dbContext.ProcessedBillingEvents.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task An_event_without_metadata_is_matched_by_the_stored_subscription_id()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddUserAsync(dbContext, UserId);
        await AddSubscriptionAsync(dbContext, new UserSubscription
        {
            UserId = UserId,
            BillingProviderCustomerId = "cus_test",
            BillingProviderSubscriptionId = "sub_test",
        });
        _provider.WebhookEvent = ProEvent("evt_no_metadata", userId: null) with { MayResolveByCustomer = false };

        var result = await CreateProcessor(dbContext).ProcessAsync("{}", "sig");

        result.Should().Be(BillingWebhookProcessingResult.Applied);
        (await ReadSubscriptionAsync(dbContext, UserId))!.Tier.Should().Be(PlanTier.Pro);
    }

    [Fact]
    public async Task The_stored_customer_id_is_used_only_for_events_known_to_be_Relio_subscriptions()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddUserAsync(dbContext, UserId);
        await AddSubscriptionAsync(dbContext, new UserSubscription { UserId = UserId, BillingProviderCustomerId = "cus_shared" });

        // Another product's invoice for the same customer: no metadata, a different subscription.
        _provider.WebhookEvent = new ParsedBillingEvent(
            "evt_other_product", "invoice.payment_failed", null, null, null,
            BillingProviderCustomerId: "cus_shared", BillingProviderSubscriptionId: "sub_other_product",
            GracePeriodEndsAtUtc: Now.UtcDateTime.AddDays(3), MayResolveByCustomer: false);
        var ignored = await CreateProcessor(dbContext).ProcessAsync("{}", "sig");

        ignored.Should().Be(BillingWebhookProcessingResult.UnknownUser);
        (await ReadSubscriptionAsync(dbContext, UserId))!.GracePeriodEndsAtUtc.Should().BeNull();

        // A Relio subscription created by hand in the Dashboard (a Relio price, no metadata).
        _provider.WebhookEvent = ProEvent("evt_dashboard_sub", userId: null, customerId: "cus_shared", subscriptionId: "sub_dashboard");
        var applied = await CreateProcessor(dbContext).ProcessAsync("{}", "sig");

        applied.Should().Be(BillingWebhookProcessingResult.Applied);
        (await ReadSubscriptionAsync(dbContext, UserId))!.Tier.Should().Be(PlanTier.Pro);
    }

    [Fact]
    public async Task An_event_for_an_account_that_no_longer_exists_is_acknowledged_and_creates_nothing()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        _provider.WebhookEvent = new ParsedBillingEvent(
            "evt_after_erasure", "customer.subscription.deleted", "deleted-user", PlanTier.Free, null,
            OccurredAtUtc: Now.UtcDateTime);

        var result = await CreateProcessor(dbContext).ProcessAsync("{}", "sig");

        result.Should().Be(BillingWebhookProcessingResult.UnknownUser);
        (await dbContext.UserSubscriptions.AnyAsync()).Should().BeFalse();
        (await dbContext.ProcessedBillingEvents.CountAsync()).Should().Be(1, "so the provider stops retrying it");
    }

    [Fact]
    public async Task Events_for_one_user_never_touch_another_users_plan()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddUserAsync(dbContext, UserId);
        await AddUserAsync(dbContext, "other-user");
        await AddSubscriptionAsync(dbContext, Pro("other-user", customerId: "cus_other"));
        _provider.WebhookEvent = new ParsedBillingEvent(
            "evt_mine", "customer.subscription.deleted", UserId, PlanTier.Free, null,
            BillingProviderCustomerId: "cus_mine", OccurredAtUtc: Now.UtcDateTime);

        await CreateProcessor(dbContext).ProcessAsync("{}", "sig");

        var other = await ReadSubscriptionAsync(dbContext, "other-user");
        other!.Tier.Should().Be(PlanTier.Pro);
        other.BillingProviderCustomerId.Should().Be("cus_other");
    }

    private BillingWebhookProcessor CreateProcessor(RelioDbContext dbContext) =>
        new(dbContext, _provider, _clock, NullLogger<BillingWebhookProcessor>.Instance);
}
