using System.Net;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Billing;
using Relio.Web.Billing;
using Stripe;
using PlanTier = Relio.Application.Billing.PlanTier;

namespace Relio.Web.Tests.Billing;

public sealed class StripeBillingProviderTests
{
    // Obviously fake credentials: nothing here is a real key, and the fake client never reaches Stripe.
    private const string ApiKey = "sk_test_fake_key_never_sent_anywhere";
    private const string WebhookSecret = "whsec_test_secret_only_used_locally";

    private const string UserId = "relio-user-1";
    private const string AccountEmail = "account@example.test";
    private const string CustomerId = "cus_test_123";
    private const string SubscriptionId = "sub_test_123";
    private const string MonthlyPriceId = "price_monthly_test";
    private const string YearlyPriceId = "price_yearly_test";
    private const string CheckoutUrl = "https://checkout.stripe.com/c/pay/cs_test_1";

    // Another product billed through the same Stripe account: its events reach Relio's endpoint too,
    // validly signed with Relio's own endpoint secret, and its subscriptions can sit on a Relio customer.
    private const string OtherProductMetadataKey = "brainy_user_id";
    private const string OtherProductUserId = "brainy-user-1";
    private const string OtherProductPriceId = "price_brainy_pro";
    private const string OtherProductSubscriptionId = "sub_brainy_1";

    // 2025-05-23T11:33:20Z: when the test webhooks are signed and when the fake clock starts.
    private const long SignedAt = 1748000000;

    private static readonly DateTime June1 = new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime July1 = new(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly string ApiVersion = (string)typeof(StripeConfiguration).Assembly
        .GetType("Stripe.ApiVersion")!
        .GetField("Current", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
        .GetValue(null)!;

    private readonly FakeTimeProvider _time = new(DateTimeOffset.FromUnixTimeSeconds(SignedAt));

    private static BillingOptions CreateOptions() => new()
    {
        Provider = BillingProviderType.Stripe,
        ApiKey = ApiKey,
        WebhookSigningSecret = WebhookSecret,
        ProMonthlyPriceId = MonthlyPriceId,
        ProYearlyPriceId = YearlyPriceId,
        CheckoutSuccessUrl = "https://app.example.test/Account/Manage/Plan?checkout=success",
        CheckoutCancelUrl = "https://app.example.test/Account/Manage/Plan?checkout=cancelled",
        PortalReturnUrl = "https://app.example.test/Account/Manage/Plan",
    };

    private StripeBillingProvider CreateProvider(
        IStripeClient? stripeClient = null,
        BillingOptions? options = null,
        ILogger<StripeBillingProvider>? logger = null) =>
        new(
            Options.Create(options ?? CreateOptions()),
            _time,
            logger ?? NullLogger<StripeBillingProvider>.Instance,
            stripeClient ?? FakeStripeClient.NeverCalled());

    // ---------------------------------------------------------------- signature verification

    [Fact]
    public async Task VerifyWebhookSignatureAsync_WithValidSignature_IsValid()
    {
        var provider = CreateProvider();
        var payload = MinimalEventJson("evt_1", "checkout.session.completed");

        var result = await provider.VerifyWebhookSignatureAsync(payload, Sign(payload));

        result.IsValid.Should().BeTrue();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task VerifyWebhookSignatureAsync_WithSignatureForADifferentPayload_IsRejected()
    {
        var provider = CreateProvider();
        var signature = Sign(MinimalEventJson("evt_1", "checkout.session.completed"));

        var result = await provider.VerifyWebhookSignatureAsync(MinimalEventJson("evt_1", "customer.subscription.deleted"), signature);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyWebhookSignatureAsync_WithAnotherEndpointsSecret_IsRejected()
    {
        var provider = CreateProvider();
        var payload = MinimalEventJson("evt_1", "checkout.session.completed");

        var result = await provider.VerifyWebhookSignatureAsync(
            payload,
            EventUtility.GenerateSignatureHeader(payload, "whsec_some_other_endpoint_fake", SignedAt));

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task VerifyWebhookSignatureAsync_WithMissingSignatureHeader_IsRejected(string header)
    {
        var provider = CreateProvider();

        var result = await provider.VerifyWebhookSignatureAsync(MinimalEventJson("evt_1", "checkout.session.completed"), header);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyWebhookSignatureAsync_WithNoSigningSecretConfigured_IsRejected()
    {
        var options = CreateOptions();
        options.WebhookSigningSecret = null;
        var provider = CreateProvider(options: options);
        var payload = MinimalEventJson("evt_1", "checkout.session.completed");

        var result = await provider.VerifyWebhookSignatureAsync(payload, Sign(payload));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyWebhookSignatureAsync_WithinFiveMinutesOfSigning_IsValid()
    {
        var provider = CreateProvider();
        var payload = MinimalEventJson("evt_1", "checkout.session.completed");
        _time.Advance(TimeSpan.FromSeconds(299));

        var result = await provider.VerifyWebhookSignatureAsync(payload, Sign(payload));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyWebhookSignatureAsync_WithSignatureOlderThanFiveMinutes_IsRejected()
    {
        // A captured delivery replayed later must not be accepted, however valid its signature.
        var provider = CreateProvider();
        var payload = MinimalEventJson("evt_1", "checkout.session.completed");
        _time.Advance(TimeSpan.FromSeconds(301));

        var result = await provider.VerifyWebhookSignatureAsync(payload, Sign(payload));

        result.IsValid.Should().BeFalse();
    }

    // ---------------------------------------------------------------- checkout.session.completed

    [Fact]
    public async Task ParseWebhookEventAsync_CheckoutSessionCompleted_LinksAccountWithoutGrantingATier()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(CheckoutCompletedPayload("evt_checkout_1", UserId));

        parsed.Should().NotBeNull();
        parsed!.ProviderEventId.Should().Be("evt_checkout_1");
        parsed.EventType.Should().Be("checkout.session.completed");
        parsed.TargetUserId.Should().Be(UserId);
        parsed.NewTier.Should().BeNull();
        parsed.BillingProviderCustomerId.Should().Be(CustomerId);
        parsed.BillingProviderSubscriptionId.Should().Be(SubscriptionId);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_CheckoutSessionCompletedInPaymentMode_IsIgnored()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(CheckoutCompletedPayload("evt_checkout_2", UserId, "payment"));

        parsed.Should().BeNull();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_CheckoutFromAnotherProduct_IsIgnored()
    {
        // client_reference_id is set by every product's checkout, so it alone must never be read as a
        // Relio user id.
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            CheckoutCompletedPayload("evt_other_checkout", OtherProductUserId, metadataKey: OtherProductMetadataKey));

        parsed.Should().BeNull();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_CheckoutWithOnlyAClientReferenceId_IsIgnored()
    {
        // Even when the client_reference_id happens to look like a Relio user id.
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            CheckoutCompletedPayload("evt_checkout_no_metadata", UserId, withMetadata: false));

        parsed.Should().BeNull();
    }

    // ---------------------------------------------------------------- customer.subscription.*

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionUpdatedActive_MapsToProWithPeriodEndAndClearsGrace()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_updated_1", "customer.subscription.updated", "active", YearlyPriceId, 1748736000));

        parsed.Should().NotBeNull();
        parsed!.TargetUserId.Should().Be(UserId);
        parsed.NewTier.Should().Be(PlanTier.Pro);
        parsed.PeriodEndsAtUtc.Should().Be(June1);
        parsed.ClearsGracePeriod.Should().BeTrue();
        parsed.BillingProviderSubscriptionId.Should().Be(SubscriptionId);
        parsed.BillingProviderCustomerId.Should().Be(CustomerId);
        parsed.MayResolveByCustomer.Should().BeTrue();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionTrialing_MapsToProAndClearsGrace()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_trial", "customer.subscription.created", "trialing", MonthlyPriceId, 1748736000));

        parsed!.NewTier.Should().Be(PlanTier.Pro);
        parsed.ClearsGracePeriod.Should().BeTrue();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionUpdatedPastDue_KeepsProWithoutClearingGrace()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_updated_2", "customer.subscription.updated", "past_due", MonthlyPriceId, 1751328000));

        parsed.Should().NotBeNull();
        parsed!.NewTier.Should().Be(PlanTier.Pro);
        parsed.PeriodEndsAtUtc.Should().Be(July1);
        parsed.ClearsGracePeriod.Should().BeFalse();
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("unpaid")]
    [InlineData("incomplete_expired")]
    [InlineData("paused")]
    public async Task ParseWebhookEventAsync_SubscriptionUpdatedToAStatusStripeNoLongerBills_MapsToFreeAndClearsGrace(string status)
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_ended", "customer.subscription.updated", status, YearlyPriceId, 1748736000));

        parsed.Should().NotBeNull();
        parsed!.NewTier.Should().Be(PlanTier.Free);
        parsed.ClearsGracePeriod.Should().BeTrue();
        parsed.MayResolveByCustomer.Should().BeTrue();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionIncomplete_LinksAccountWithoutChangingTier()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_incomplete", "customer.subscription.created", "incomplete", YearlyPriceId, 1748736000));

        parsed.Should().NotBeNull();
        parsed!.NewTier.Should().BeNull();
        parsed.ClearsGracePeriod.Should().BeFalse();
        parsed.BillingProviderSubscriptionId.Should().Be(SubscriptionId);
        parsed.BillingProviderCustomerId.Should().Be(CustomerId);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionEvent_CarriesTheEventCreationTime()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_created_at", "customer.subscription.updated", "active", YearlyPriceId, 1748736000, created: 1751328000));

        parsed!.OccurredAtUtc.Should().Be(July1);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionDeleted_MapsToFreeDowngrade()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_deleted_1", "customer.subscription.deleted", "canceled", YearlyPriceId, 1748736000));

        parsed.Should().NotBeNull();
        parsed!.NewTier.Should().Be(PlanTier.Free);
        parsed.ClearsGracePeriod.Should().BeTrue();
        parsed.TargetUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_RelioSubscriptionOnAnUnconfiguredPrice_DoesNotGrantPro()
    {
        // Relio's metadata makes it Relio's, but a price that is not configured never grants Pro.
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_unknown_price", "customer.subscription.updated", "active", "price_not_configured", 1748736000));

        parsed.Should().NotBeNull();
        parsed!.NewTier.Should().Be(PlanTier.Free);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionWithoutMetadataOnARelioPrice_LeavesTheUserToTheStoredCustomer()
    {
        // A subscription created by hand in the Dashboard: the processor resolves the user from the
        // stored customer id, which MayResolveByCustomer allows.
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_no_metadata", "customer.subscription.updated", "active", YearlyPriceId, 1748736000, metadataUserId: null));

        parsed.Should().NotBeNull();
        parsed!.TargetUserId.Should().BeNull();
        parsed.NewTier.Should().Be(PlanTier.Pro);
        parsed.MayResolveByCustomer.Should().BeTrue();
        parsed.BillingProviderCustomerId.Should().Be(CustomerId);
        parsed.BillingProviderSubscriptionId.Should().Be(SubscriptionId);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionWithBlankRelioMetadata_HasNoTargetUser()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_blank_metadata", "customer.subscription.updated", "active", YearlyPriceId, 1748736000, metadataUserId: "  "));

        parsed!.TargetUserId.Should().BeNull();
        parsed.MayResolveByCustomer.Should().BeTrue();
    }

    [Theory]
    [InlineData("customer.subscription.created")]
    [InlineData("customer.subscription.updated")]
    [InlineData("customer.subscription.deleted")]
    public async Task ParseWebhookEventAsync_AnotherProductsSubscriptionOnARelioCustomer_IsIgnored(string eventType)
    {
        // Same Stripe customer as a paying Relio user. Mapping it would downgrade the Relio user (an
        // unknown price maps to Free, a deletion to Free) or relink them to another product's subscription.
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(SubscriptionPayload(
            "evt_other_subscription",
            eventType,
            "active",
            OtherProductPriceId,
            1748736000,
            metadataUserId: OtherProductUserId,
            metadataKey: OtherProductMetadataKey,
            subscriptionId: OtherProductSubscriptionId));

        parsed.Should().BeNull();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionWithNoMetadataAndAnUnconfiguredPrice_IsIgnored()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(SubscriptionPayload(
            "evt_unmarked_subscription",
            "customer.subscription.deleted",
            "canceled",
            OtherProductPriceId,
            1748736000,
            metadataUserId: null,
            subscriptionId: OtherProductSubscriptionId));

        parsed.Should().BeNull();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionCancelledAtPeriodEnd_CarriesTheScheduledEnd()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_cancel_scheduled", "customer.subscription.updated", "active", YearlyPriceId, 1748736000, cancelAtPeriodEnd: true));

        parsed!.NewTier.Should().Be(PlanTier.Pro);
        parsed.HasCancellationSchedule.Should().BeTrue();
        parsed.CancelsAtUtc.Should().Be(June1);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionWithAnExplicitCancelAt_CarriesThatDate()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_cancel_at", "customer.subscription.updated", "active", YearlyPriceId, 1751328000, cancelAt: 1748736000));

        parsed!.HasCancellationSchedule.Should().BeTrue();
        parsed.CancelsAtUtc.Should().Be(June1);
        parsed.PeriodEndsAtUtc.Should().Be(July1);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_ActiveSubscriptionWithoutCancellation_ClearsTheSchedule()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_resumed", "customer.subscription.updated", "active", YearlyPriceId, 1748736000));

        parsed!.HasCancellationSchedule.Should().BeTrue();
        parsed.CancelsAtUtc.Should().BeNull();
    }

    // ---------------------------------------------------------------- invoices

    [Fact]
    public async Task ParseWebhookEventAsync_InvoicePaymentFailedForARelioPrice_SetsGracePeriodAndMayResolveByCustomer()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            InvoicePayload("evt_invoice_failed_1", "invoice.payment_failed", nextPaymentAttempt: 1751328000, linePriceId: YearlyPriceId));

        parsed.Should().NotBeNull();
        parsed!.NewTier.Should().BeNull();
        parsed.TargetUserId.Should().BeNull();
        parsed.MayResolveByCustomer.Should().BeTrue();
        parsed.BillingProviderCustomerId.Should().Be(CustomerId);
        parsed.BillingProviderSubscriptionId.Should().Be("sub_unrecorded");
        // Stripe's next retry (2025-07-01) plus a day for that retry's own webhook to arrive.
        parsed.GracePeriodEndsAtUtc.Should().Be(new DateTime(2025, 7, 2, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ParseWebhookEventAsync_InvoicePaymentFailedWithNoFurtherRetry_GivesSevenDaysFromNow()
    {
        var provider = CreateProvider();
        _time.Advance(TimeSpan.FromHours(5));
        var expected = _time.GetUtcNow().UtcDateTime + TimeSpan.FromDays(7);

        var parsed = await provider.ParseWebhookEventAsync(
            InvoicePayload("evt_invoice_failed_final", "invoice.payment_failed", linePriceId: YearlyPriceId));

        parsed!.GracePeriodEndsAtUtc.Should().Be(expected);
        parsed.ClearsGracePeriod.Should().BeFalse();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_InvoicePaid_ClearsGracePeriodWithoutGrantingATier()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            InvoicePayload("evt_invoice_paid_1", "invoice.paid", periodEnd: 1748736000, linePriceId: YearlyPriceId));

        // Tier and renewal come from subscription events only: an invoice can arrive after the
        // subscription was deleted, and its period end is the period just billed.
        parsed.Should().NotBeNull();
        parsed!.NewTier.Should().BeNull();
        parsed.PeriodEndsAtUtc.Should().BeNull();
        parsed.GracePeriodEndsAtUtc.Should().BeNull();
        parsed.ClearsGracePeriod.Should().BeTrue();
        parsed.MayResolveByCustomer.Should().BeTrue();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_InvoiceWithRelioSubscriptionMetadata_NamesTheUserFromTheMetadata()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            InvoicePayload("evt_invoice_metadata", "invoice.paid", periodEnd: 1748736000, subscriptionMetadataUserId: UserId));

        parsed.Should().NotBeNull();
        parsed!.TargetUserId.Should().Be(UserId);
        // No Relio price on the lines, so the customer id is neither reported nor usable.
        parsed.MayResolveByCustomer.Should().BeFalse();
        parsed.BillingProviderCustomerId.Should().BeNull();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_InvoiceWithOnlyASubscriptionId_LeavesTheMatchToTheStoredSubscription()
    {
        // No metadata snapshot and no configured price: only the subscription id Relio stored can say
        // whose it is, and the customer id must not be used.
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(
            InvoicePayload("evt_invoice_stored_sub", "invoice.payment_failed", nextPaymentAttempt: 1751328000, subscriptionId: SubscriptionId));

        parsed.Should().NotBeNull();
        parsed!.TargetUserId.Should().BeNull();
        parsed.BillingProviderSubscriptionId.Should().Be(SubscriptionId);
        parsed.BillingProviderCustomerId.Should().BeNull();
        parsed.MayResolveByCustomer.Should().BeFalse();
    }

    [Theory]
    [InlineData("invoice.paid")]
    [InlineData("invoice.payment_failed")]
    public async Task ParseWebhookEventAsync_InvoiceWithNoMetadataNoSubscriptionAndNoRelioPrice_IsIgnored(string eventType)
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(InvoicePayload(
            "evt_unmarked_invoice",
            eventType,
            nextPaymentAttempt: 1751328000,
            subscriptionId: null,
            linePriceId: OtherProductPriceId));

        parsed.Should().BeNull();
    }

    [Theory]
    [InlineData("invoice.paid")]
    [InlineData("invoice.payment_failed")]
    public async Task ParseWebhookEventAsync_AnotherProductsInvoiceOnARelioCustomer_CannotReachTheRelioUser(string eventType)
    {
        // A failed invoice for another product must not put the Relio user into a grace period, and a
        // paid one must not clear it. The provider cannot know which subscription ids Relio stored, so it
        // hands the event on for the processor to match by subscription id (which fails for another
        // product's), but with no Relio user, no customer id and no permission to fall back to the customer.
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(InvoicePayload(
            "evt_other_invoice",
            eventType,
            nextPaymentAttempt: 1751328000,
            periodEnd: 1748736000,
            subscriptionId: OtherProductSubscriptionId,
            subscriptionMetadataUserId: OtherProductUserId,
            metadataKey: OtherProductMetadataKey,
            linePriceId: OtherProductPriceId));

        parsed.Should().NotBeNull();
        parsed!.TargetUserId.Should().BeNull();
        parsed.BillingProviderCustomerId.Should().BeNull();
        parsed.MayResolveByCustomer.Should().BeFalse();
        parsed.BillingProviderSubscriptionId.Should().Be(OtherProductSubscriptionId);
        parsed.NewTier.Should().BeNull();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_AnotherProductsInvoiceWithoutASubscription_IsIgnored()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(InvoicePayload(
            "evt_other_invoice_one_off",
            "invoice.payment_failed",
            nextPaymentAttempt: 1751328000,
            subscriptionId: null,
            subscriptionMetadataUserId: OtherProductUserId,
            metadataKey: OtherProductMetadataKey,
            linePriceId: OtherProductPriceId));

        parsed.Should().BeNull();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_UnrecognizedEventType_ReturnsNull()
    {
        var provider = CreateProvider();

        var parsed = await provider.ParseWebhookEventAsync(MinimalEventJson("evt_x", "customer.created"));

        parsed.Should().BeNull();
    }

    // ---------------------------------------------------------------- CreateCheckoutSessionAsync

    [Theory]
    [InlineData(BillingInterval.Monthly, MonthlyPriceId)]
    [InlineData(BillingInterval.Yearly, YearlyPriceId)]
    public async Task CreateCheckoutSessionAsync_ReturnsStripesHostedCheckoutUrlForTheIntervalsPrice(BillingInterval interval, string expectedPriceId)
    {
        var fakeClient = new FakeStripeClient(_ => CheckoutSession());
        var provider = CreateProvider(fakeClient);

        var result = await provider.CreateCheckoutSessionAsync(new BillingCheckoutRequest(UserId, interval, null, null));

        result.Supported.Should().BeTrue();
        result.RedirectUrl.Should().Be(CheckoutUrl);
        result.UnsupportedReason.Should().BeNull();
        fakeClient.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post && r.Path == "/v1/checkout/sessions");
        var options = SentCheckoutOptions(fakeClient);
        options.Mode.Should().Be("subscription");
        options.LineItems.Should().ContainSingle().Which.Price.Should().Be(expectedPriceId);
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_StampsTheRelioUserOnTheSessionAndTheSubscription()
    {
        var fakeClient = new FakeStripeClient(_ => CheckoutSession());
        var provider = CreateProvider(fakeClient);

        await provider.CreateCheckoutSessionAsync(new BillingCheckoutRequest(UserId, BillingInterval.Yearly, null, AccountEmail));

        var options = SentCheckoutOptions(fakeClient);
        StripeBillingProvider.UserIdMetadataKey.Should().Be("relio_user_id");
        options.ClientReferenceId.Should().Be(UserId);
        options.Metadata.Should().Equal(new Dictionary<string, string> { ["relio_user_id"] = UserId });
        options.SubscriptionData.Metadata.Should().Equal(new Dictionary<string, string> { ["relio_user_id"] = UserId });
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_ForAFirstPurchase_SeedsTheCustomerWithTheAccountEmail()
    {
        var fakeClient = new FakeStripeClient(_ => CheckoutSession());
        var provider = CreateProvider(fakeClient);

        await provider.CreateCheckoutSessionAsync(new BillingCheckoutRequest(UserId, BillingInterval.Yearly, null, AccountEmail));

        // No customer yet, so there are no subscriptions to check either.
        fakeClient.Requests.Should().ContainSingle();
        var options = SentCheckoutOptions(fakeClient);
        options.CustomerEmail.Should().Be(AccountEmail);
        options.Customer.Should().BeNull();
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_WithABlankExistingCustomerId_TreatsItAsAFirstPurchase()
    {
        var fakeClient = new FakeStripeClient(_ => CheckoutSession());
        var provider = CreateProvider(fakeClient);

        await provider.CreateCheckoutSessionAsync(new BillingCheckoutRequest(UserId, BillingInterval.Yearly, "  ", AccountEmail));

        fakeClient.Requests.Should().ContainSingle();
        var options = SentCheckoutOptions(fakeClient);
        options.Customer.Should().BeNull();
        options.CustomerEmail.Should().Be(AccountEmail);
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_ForAnExistingCustomer_SendsNoEmailAlongsideTheCustomerId()
    {
        // Stripe rejects customer and customer_email together, so the account email is dropped once the
        // user has a customer record.
        var fakeClient = new FakeStripeClient(type => type == typeof(StripeList<Subscription>)
            ? Subscriptions()
            : CheckoutSession());
        var provider = CreateProvider(fakeClient);

        await provider.CreateCheckoutSessionAsync(new BillingCheckoutRequest(UserId, BillingInterval.Yearly, CustomerId, AccountEmail));

        var options = SentCheckoutOptions(fakeClient);
        options.Customer.Should().Be(CustomerId);
        options.CustomerEmail.Should().BeNull();
        var list = fakeClient.SentOptions.OfType<SubscriptionListOptions>().Should().ContainSingle().Subject;
        list.Customer.Should().Be(CustomerId);
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_AddsTheSessionIdPlaceholderToTheSuccessUrl()
    {
        var fakeClient = new FakeStripeClient(_ => CheckoutSession());
        var provider = CreateProvider(fakeClient);

        await provider.CreateCheckoutSessionAsync(new BillingCheckoutRequest(UserId, BillingInterval.Monthly, null, null));

        var options = SentCheckoutOptions(fakeClient);
        options.SuccessUrl.Should().Be("https://app.example.test/Account/Manage/Plan?checkout=success&session_id={CHECKOUT_SESSION_ID}");
        // Already carries a checkout outcome, so it is left as configured.
        options.CancelUrl.Should().Be("https://app.example.test/Account/Manage/Plan?checkout=cancelled");
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_WithPlainReturnUrls_AddsTheQueryItself()
    {
        var configured = CreateOptions();
        configured.CheckoutSuccessUrl = "https://app.example.test/Account/Manage/Plan";
        configured.CheckoutCancelUrl = "https://app.example.test/Account/Manage/Plan";
        var fakeClient = new FakeStripeClient(_ => CheckoutSession());
        var provider = CreateProvider(fakeClient, configured);

        await provider.CreateCheckoutSessionAsync(new BillingCheckoutRequest(UserId, BillingInterval.Monthly, null, null));

        var options = SentCheckoutOptions(fakeClient);
        options.SuccessUrl.Should().Be("https://app.example.test/Account/Manage/Plan?session_id={CHECKOUT_SESSION_ID}");
        options.CancelUrl.Should().Be("https://app.example.test/Account/Manage/Plan?checkout=cancelled");
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_WithThePlaceholderAlreadyInTheSuccessUrl_LeavesItAsConfigured()
    {
        var configured = CreateOptions();
        configured.CheckoutSuccessUrl = "https://app.example.test/Account/Manage/Plan?session_id={CHECKOUT_SESSION_ID}";
        var fakeClient = new FakeStripeClient(_ => CheckoutSession());
        var provider = CreateProvider(fakeClient, configured);

        await provider.CreateCheckoutSessionAsync(new BillingCheckoutRequest(UserId, BillingInterval.Monthly, null, null));

        SentCheckoutOptions(fakeClient).SuccessUrl.Should().Be(configured.CheckoutSuccessUrl);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("trialing")]
    [InlineData("past_due")]
    public async Task CreateCheckoutSessionAsync_WhenStripeAlreadyHasALiveRelioSubscription_IsRefused(string status)
    {
        // The stored tier still says Free because the first checkout's webhook has not landed.
        var fakeClient = new FakeStripeClient(type => type == typeof(StripeList<Subscription>)
            ? Subscriptions(new Subscription { Id = SubscriptionId, Status = status, Metadata = RelioMetadata() })
            : throw new InvalidOperationException("Should not create a checkout session."));
        var provider = CreateProvider(fakeClient);

        var result = await provider.CreateCheckoutSessionAsync(new BillingCheckoutRequest(UserId, BillingInterval.Monthly, CustomerId, AccountEmail));

        result.Supported.Should().BeFalse();
        result.RedirectUrl.Should().BeNull();
        result.UnsupportedReason.Should().NotBeNullOrWhiteSpace();
        fakeClient.Requests.Should().NotContain(r => r.Path == "/v1/checkout/sessions");
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_WhenALiveSubscriptionBillsARelioPriceWithoutMetadata_IsRefused()
    {
        var fakeClient = new FakeStripeClient(type => type == typeof(StripeList<Subscription>)
            ? Subscriptions(SubscriptionOnPrice("sub_by_hand", "active", YearlyPriceId))
            : throw new InvalidOperationException("Should not create a checkout session."));
        var provider = CreateProvider(fakeClient);

        var result = await provider.CreateCheckoutSessionAsync(new BillingCheckoutRequest(UserId, BillingInterval.Monthly, CustomerId, null));

        result.Supported.Should().BeFalse();
        fakeClient.Requests.Should().NotContain(r => r.Path == "/v1/checkout/sessions");
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_WhenTheCustomerOnlyHasAnotherProductsSubscription_OpensCheckout()
    {
        var fakeClient = new FakeStripeClient(type => type == typeof(StripeList<Subscription>)
            ? Subscriptions(
                new Subscription { Id = OtherProductSubscriptionId, Status = "active", Metadata = new() { [OtherProductMetadataKey] = OtherProductUserId } },
                SubscriptionOnPrice("sub_brainy_2", "active", OtherProductPriceId))
            : CheckoutSession());
        var provider = CreateProvider(fakeClient);

        var result = await provider.CreateCheckoutSessionAsync(new BillingCheckoutRequest(UserId, BillingInterval.Monthly, CustomerId, null));

        result.Supported.Should().BeTrue();
        result.RedirectUrl.Should().Be(CheckoutUrl);
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("incomplete_expired")]
    [InlineData("unpaid")]
    public async Task CreateCheckoutSessionAsync_WhenTheCustomersRelioSubscriptionHasEnded_OpensCheckout(string status)
    {
        var fakeClient = new FakeStripeClient(type => type == typeof(StripeList<Subscription>)
            ? Subscriptions(new Subscription { Id = SubscriptionId, Status = status, Metadata = RelioMetadata() })
            : CheckoutSession());
        var provider = CreateProvider(fakeClient);

        var result = await provider.CreateCheckoutSessionAsync(new BillingCheckoutRequest(UserId, BillingInterval.Monthly, CustomerId, null));

        result.Supported.Should().BeTrue();
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_WhenListingSubscriptionsFails_IsUnsupportedWithoutOpeningCheckout()
    {
        var fakeClient = FakeStripeClient.PerRequest(request => request.ResponseType == typeof(StripeList<Subscription>)
            ? throw new StripeException("Stripe is unavailable.")
            : CheckoutSession());
        var provider = CreateProvider(fakeClient);

        var result = await provider.CreateCheckoutSessionAsync(new BillingCheckoutRequest(UserId, BillingInterval.Monthly, CustomerId, null));

        result.Supported.Should().BeFalse();
        result.UnsupportedReason.Should().NotBeNullOrWhiteSpace();
        fakeClient.Requests.Should().NotContain(r => r.Path == "/v1/checkout/sessions");
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_WhenStripeFails_IsUnsupportedAndNeverLogsStripesMessage()
    {
        // Stripe's error messages can quote submitted values such as the email address.
        var logger = new CapturingLogger<StripeBillingProvider>();
        var fakeClient = new FakeStripeClient(_ => throw new StripeException(
            HttpStatusCode.BadRequest,
            new StripeError { Code = "email_invalid" },
            $"Invalid email address: {AccountEmail}"));
        var provider = CreateProvider(fakeClient, logger: logger);

        var result = await provider.CreateCheckoutSessionAsync(new BillingCheckoutRequest(UserId, BillingInterval.Yearly, null, AccountEmail));

        result.Supported.Should().BeFalse();
        result.RedirectUrl.Should().BeNull();
        result.UnsupportedReason.Should().NotContain(AccountEmail);
        logger.Messages.Should().NotBeEmpty();
        logger.Messages.Should().NotContain(message => message.Contains(AccountEmail, StringComparison.Ordinal));
        logger.Messages.Should().NotContain(message => message.Contains(ApiKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_WhenStripeReturnsNoUrl_IsUnsupported()
    {
        var fakeClient = new FakeStripeClient(_ => new Stripe.Checkout.Session { Id = "cs_test_1", Url = null });
        var provider = CreateProvider(fakeClient);

        var result = await provider.CreateCheckoutSessionAsync(new BillingCheckoutRequest(UserId, BillingInterval.Yearly, null, null));

        result.Supported.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task CreateCheckoutSessionAsync_WithoutAUserId_ThrowsBeforeCallingStripe(string userId)
    {
        var fakeClient = FakeStripeClient.NeverCalled();
        var provider = CreateProvider(fakeClient);

        var act = () => provider.CreateCheckoutSessionAsync(new BillingCheckoutRequest(userId, BillingInterval.Yearly, null, null));

        await act.Should().ThrowAsync<ArgumentException>();
        fakeClient.Requests.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- GetCompletedCheckoutAsync

    [Fact]
    public async Task GetCompletedCheckoutAsync_ForTheUsersCompletedSession_MapsTheSubscriptionToPro()
    {
        var fakeClient = new FakeStripeClient(_ => CompletedCheckoutSession(UserId));
        var provider = CreateProvider(fakeClient);

        var parsed = await provider.GetCompletedCheckoutAsync(UserId, "cs_test_1");

        parsed.Should().NotBeNull();
        parsed!.TargetUserId.Should().Be(UserId);
        parsed.NewTier.Should().Be(PlanTier.Pro);
        parsed.BillingProviderCustomerId.Should().Be(CustomerId);
        parsed.BillingProviderSubscriptionId.Should().Be(SubscriptionId);
        parsed.PeriodEndsAtUtc.Should().Be(June1);
        parsed.ClearsGracePeriod.Should().BeTrue();
        // Applied regardless of order: the user is looking at Stripe's current state.
        parsed.OccurredAtUtc.Should().BeNull();
        fakeClient.Requests.Should().Contain(r => r.Method == HttpMethod.Get && r.Path == "/v1/checkout/sessions/cs_test_1");
    }

    [Fact]
    public async Task GetCompletedCheckoutAsync_ForAnotherUsersSession_ReturnsNull()
    {
        var provider = CreateProvider(new FakeStripeClient(_ => CompletedCheckoutSession("someone-else")));

        (await provider.GetCompletedCheckoutAsync(UserId, "cs_test_1")).Should().BeNull();
    }

    [Fact]
    public async Task GetCompletedCheckoutAsync_WhenOnlyTheClientReferenceIdMatches_ReturnsNull()
    {
        // client_reference_id is a generic field another product sets too; only Relio's metadata counts.
        var session = CompletedCheckoutSession(UserId);
        session.Metadata = new Dictionary<string, string>();
        var provider = CreateProvider(new FakeStripeClient(_ => session));

        (await provider.GetCompletedCheckoutAsync(UserId, "cs_test_1")).Should().BeNull();
    }

    [Fact]
    public async Task GetCompletedCheckoutAsync_ForAnotherProductsSessionWithTheSameClientReference_ReturnsNull()
    {
        var session = CompletedCheckoutSession(UserId);
        session.Metadata = new Dictionary<string, string> { [OtherProductMetadataKey] = UserId };
        var provider = CreateProvider(new FakeStripeClient(_ => session));

        (await provider.GetCompletedCheckoutAsync(UserId, "cs_test_1")).Should().BeNull();
    }

    [Theory]
    [InlineData("open", "subscription")]
    [InlineData("expired", "subscription")]
    [InlineData("complete", "payment")]
    public async Task GetCompletedCheckoutAsync_ForASessionThatIsNotACompletedSubscription_ReturnsNull(string status, string mode)
    {
        var session = CompletedCheckoutSession(UserId);
        session.Status = status;
        session.Mode = mode;
        var provider = CreateProvider(new FakeStripeClient(_ => session));

        (await provider.GetCompletedCheckoutAsync(UserId, "cs_test_1")).Should().BeNull();
    }

    [Fact]
    public async Task GetCompletedCheckoutAsync_WhenStripeFails_ReturnsNull()
    {
        var provider = CreateProvider(new FakeStripeClient(_ => throw new StripeException("No such checkout session.")));

        (await provider.GetCompletedCheckoutAsync(UserId, "cs_test_1")).Should().BeNull();
    }

    // ---------------------------------------------------------------- portal, email, cancellation

    [Fact]
    public async Task CreatePortalSessionAsync_ReturnsStripesHostedPortalUrlForTheCustomer()
    {
        const string portalUrl = "https://billing.stripe.com/p/session/test_1";
        var fakeClient = new FakeStripeClient(_ => new Stripe.BillingPortal.Session { Id = "bps_1", Url = portalUrl });
        var provider = CreateProvider(fakeClient);

        var result = await provider.CreatePortalSessionAsync(CustomerId);

        result.Supported.Should().BeTrue();
        result.RedirectUrl.Should().Be(portalUrl);
        fakeClient.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post && r.Path == "/v1/billing_portal/sessions");
        var options = fakeClient.SentOptions.OfType<Stripe.BillingPortal.SessionCreateOptions>().Should().ContainSingle().Subject;
        options.Customer.Should().Be(CustomerId);
        options.ReturnUrl.Should().Be("https://app.example.test/Account/Manage/Plan");
    }

    [Fact]
    public async Task CreatePortalSessionAsync_WhenStripeFails_IsUnsupported()
    {
        var provider = CreateProvider(new FakeStripeClient(_ => throw new StripeException("Stripe is unavailable.")));

        var result = await provider.CreatePortalSessionAsync(CustomerId);

        result.Supported.Should().BeFalse();
        result.RedirectUrl.Should().BeNull();
        result.UnsupportedReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreatePortalSessionAsync_WithoutACustomerId_ThrowsBeforeCallingStripe()
    {
        var fakeClient = FakeStripeClient.NeverCalled();
        var provider = CreateProvider(fakeClient);

        var act = () => provider.CreatePortalSessionAsync(" ");

        await act.Should().ThrowAsync<ArgumentException>();
        fakeClient.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateCustomerEmailAsync_UpdatesTheStripeCustomer()
    {
        var fakeClient = new FakeStripeClient(_ => new Customer { Id = CustomerId });
        var provider = CreateProvider(fakeClient);

        (await provider.UpdateCustomerEmailAsync(CustomerId, "new@example.test")).Should().BeTrue();

        fakeClient.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post && r.Path == $"/v1/customers/{CustomerId}");
        fakeClient.SentOptions.Should().ContainSingle().Which.Should().BeOfType<CustomerUpdateOptions>()
            .Which.Email.Should().Be("new@example.test");
    }

    [Fact]
    public async Task UpdateCustomerEmailAsync_WhenStripeFails_ReturnsFalseAndNeverLogsTheEmail()
    {
        var logger = new CapturingLogger<StripeBillingProvider>();
        var provider = CreateProvider(
            new FakeStripeClient(_ => throw new StripeException($"Invalid email: new@example.test")),
            logger: logger);

        (await provider.UpdateCustomerEmailAsync(CustomerId, "new@example.test")).Should().BeFalse();

        logger.Messages.Should().NotContain(message => message.Contains("new@example.test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelSubscriptionsAsync_CancelsEveryLiveRelioSubscriptionOnTheCustomer()
    {
        var fakeClient = new FakeStripeClient(type => type == typeof(StripeList<Subscription>)
            ? Subscriptions(
                new Subscription { Id = "sub_live", Status = "active", Metadata = RelioMetadata() },
                new Subscription { Id = "sub_duplicate", Status = "past_due", Metadata = RelioMetadata() },
                SubscriptionOnPrice("sub_by_hand", "trialing", MonthlyPriceId),
                new Subscription { Id = "sub_expired", Status = "incomplete_expired", Metadata = RelioMetadata() },
                new Subscription { Id = "sub_cancelled", Status = "canceled", Metadata = RelioMetadata() },
                // Another product's subscriptions on the same customer must keep running.
                new Subscription { Id = OtherProductSubscriptionId, Status = "active", Metadata = new() { [OtherProductMetadataKey] = OtherProductUserId } },
                SubscriptionOnPrice("sub_brainy_2", "active", OtherProductPriceId))
            : new Subscription { Status = "canceled" });
        var provider = CreateProvider(fakeClient);

        var result = await provider.CancelSubscriptionsAsync(CustomerId);

        result.Succeeded.Should().BeTrue();
        fakeClient.SentOptions.OfType<SubscriptionListOptions>().Should().ContainSingle().Which.Customer.Should().Be(CustomerId);
        fakeClient.Requests.Where(r => r.Method == HttpMethod.Delete).Select(r => r.Path).Should().Equal(
            "/v1/subscriptions/sub_live",
            "/v1/subscriptions/sub_duplicate",
            "/v1/subscriptions/sub_by_hand");
    }

    [Fact]
    public async Task CancelSubscriptionsAsync_WithNothingToCancel_Succeeds()
    {
        var fakeClient = new FakeStripeClient(_ => Subscriptions());
        var provider = CreateProvider(fakeClient);

        var result = await provider.CancelSubscriptionsAsync(CustomerId);

        result.Succeeded.Should().BeTrue();
        fakeClient.Requests.Should().NotContain(r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task CancelSubscriptionsAsync_WhenASubscriptionIsAlreadyGone_StillSucceedsAndCancelsTheRest()
    {
        var fakeClient = FakeStripeClient.PerRequest(request => request switch
        {
            { ResponseType: var type } when type == typeof(StripeList<Subscription>) => Subscriptions(
                new Subscription { Id = "sub_gone", Status = "active", Metadata = RelioMetadata() },
                new Subscription { Id = "sub_live", Status = "active", Metadata = RelioMetadata() }),
            { Path: "/v1/subscriptions/sub_gone" } => throw new StripeException(
                HttpStatusCode.NotFound,
                new StripeError { Code = "resource_missing" },
                "No such subscription."),
            _ => new Subscription { Status = "canceled" },
        });
        var provider = CreateProvider(fakeClient);

        var result = await provider.CancelSubscriptionsAsync(CustomerId);

        result.Succeeded.Should().BeTrue();
        fakeClient.Requests.Should().Contain(r => r.Method == HttpMethod.Delete && r.Path == "/v1/subscriptions/sub_live");
    }

    [Fact]
    public async Task CancelSubscriptionsAsync_WhenStripeFails_ReportsFailure()
    {
        var provider = CreateProvider(new FakeStripeClient(_ => throw new StripeException("Stripe is unavailable.")));

        var result = await provider.CancelSubscriptionsAsync(CustomerId);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CancelSubscriptionsAsync_WhenACancellationFails_ReportsFailure()
    {
        var fakeClient = FakeStripeClient.PerRequest(request => request.ResponseType == typeof(StripeList<Subscription>)
            ? Subscriptions(new Subscription { Id = "sub_live", Status = "active", Metadata = RelioMetadata() })
            : throw new StripeException(HttpStatusCode.InternalServerError, new StripeError { Code = "api_error" }, "Stripe is unavailable."));
        var provider = CreateProvider(fakeClient);

        var result = await provider.CancelSubscriptionsAsync(CustomerId);

        result.Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"id\": ")]
    [InlineData("[]")]
    public async Task ParseWebhookEventAsync_WithAPayloadThatIsNotAStripeEvent_ReturnsNullInsteadOfThrowing(string payload)
    {
        var parsed = await CreateProvider().ParseWebhookEventAsync(payload);

        parsed.Should().BeNull();
    }

    [Fact]
    public void IsEnabled_IsTrue()
    {
        CreateProvider().IsEnabled.Should().BeTrue();
    }

    // ---------------------------------------------------------------- helpers

    private static string Sign(string payload) =>
        EventUtility.GenerateSignatureHeader(payload, WebhookSecret, SignedAt);

    private static Stripe.Checkout.SessionCreateOptions SentCheckoutOptions(FakeStripeClient client) =>
        client.SentOptions.OfType<Stripe.Checkout.SessionCreateOptions>().Should().ContainSingle().Subject;

    private static Stripe.Checkout.Session CheckoutSession() => new() { Id = "cs_test_1", Url = CheckoutUrl };

    private static StripeList<Subscription> Subscriptions(params Subscription[] subscriptions) =>
        new() { Data = [.. subscriptions] };

    private static Subscription SubscriptionOnPrice(string id, string status, string priceId) => new()
    {
        Id = id,
        Status = status,
        Metadata = new Dictionary<string, string>(),
        Items = new StripeList<SubscriptionItem> { Data = [new SubscriptionItem { Price = new Price { Id = priceId } }] },
    };

    private static Stripe.Checkout.Session CompletedCheckoutSession(string metadataUserId) => new()
    {
        Id = "cs_test_1",
        Mode = "subscription",
        Status = "complete",
        ClientReferenceId = metadataUserId,
        Metadata = new Dictionary<string, string> { [StripeBillingProvider.UserIdMetadataKey] = metadataUserId },
        CustomerId = CustomerId,
        Subscription = new Subscription
        {
            Id = SubscriptionId,
            Status = "active",
            Items = new StripeList<SubscriptionItem>
            {
                Data = [new SubscriptionItem { Price = new Price { Id = YearlyPriceId }, CurrentPeriodEnd = June1 }],
            },
        },
    };

    private static Dictionary<string, string> RelioMetadata() => new() { [StripeBillingProvider.UserIdMetadataKey] = UserId };

    private static string MinimalEventJson(string id, string type) =>
        $$"""
        {
          "id": "{{id}}",
          "object": "event",
          "api_version": "{{ApiVersion}}",
          "type": "{{type}}",
          "data": { "object": { "id": "obj_1", "object": "customer" } }
        }
        """;

    private static string CheckoutCompletedPayload(
        string eventId,
        string? clientReferenceId,
        string mode = "subscription",
        string metadataKey = StripeBillingProvider.UserIdMetadataKey,
        bool withMetadata = true) =>
        $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "{{ApiVersion}}",
          "type": "checkout.session.completed",
          "data": {
            "object": {
              "id": "cs_test_1",
              "object": "checkout.session",
              "mode": "{{mode}}",
              "client_reference_id": {{(clientReferenceId is null ? "null" : $"\"{clientReferenceId}\"")}},
              "metadata": {{MetadataJson(metadataKey, withMetadata ? clientReferenceId : null)}},
              "customer": "{{CustomerId}}",
              "subscription": "{{SubscriptionId}}"
            }
          }
        }
        """;

    private static string SubscriptionPayload(
        string eventId,
        string eventType,
        string status,
        string priceId,
        long currentPeriodEnd,
        string? metadataUserId = UserId,
        long created = 1748000000,
        bool cancelAtPeriodEnd = false,
        long? cancelAt = null,
        string metadataKey = StripeBillingProvider.UserIdMetadataKey,
        string subscriptionId = SubscriptionId) =>
        $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "{{ApiVersion}}",
          "created": {{created}},
          "type": "{{eventType}}",
          "data": {
            "object": {
              "id": "{{subscriptionId}}",
              "object": "subscription",
              "customer": "{{CustomerId}}",
              "status": "{{status}}",
              "cancel_at_period_end": {{(cancelAtPeriodEnd ? "true" : "false")}},
              "cancel_at": {{(cancelAt.HasValue ? cancelAt.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}},
              "metadata": {{MetadataJson(metadataKey, metadataUserId)}},
              "items": {
                "object": "list",
                "data": [
                  {
                    "id": "si_test_1",
                    "object": "subscription_item",
                    "current_period_end": {{currentPeriodEnd}},
                    "price": { "id": "{{priceId}}", "object": "price" }
                  }
                ]
              }
            }
          }
        }
        """;

    /// <summary>
    /// An invoice event in the shape Stripe's current API sends: the billed subscription (and a
    /// snapshot of its metadata) under <c>parent.subscription_details</c>, and each line's price under
    /// <c>pricing.price_details</c>. A null <paramref name="subscriptionId"/> leaves the subscription out
    /// (a one-off invoice), keeping only the metadata snapshot.
    /// </summary>
    private static string InvoicePayload(
        string eventId,
        string eventType,
        long? nextPaymentAttempt = null,
        long? periodEnd = null,
        string? subscriptionId = "sub_unrecorded",
        string? subscriptionMetadataUserId = null,
        string metadataKey = StripeBillingProvider.UserIdMetadataKey,
        string linePriceId = "price_unconfigured") =>
        $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "{{ApiVersion}}",
          "created": 1748000000,
          "type": "{{eventType}}",
          "data": {
            "object": {
              "id": "in_test_1",
              "object": "invoice",
              "customer": "{{CustomerId}}",
              "parent": {
                "type": "subscription_details",
                "subscription_details": {
                  "subscription": {{(subscriptionId is null ? "null" : $"\"{subscriptionId}\"")}},
                  "metadata": {{MetadataJson(metadataKey, subscriptionMetadataUserId)}}
                }
              },
              "lines": {
                "object": "list",
                "data": [
                  {
                    "id": "il_test_1",
                    "object": "line_item",
                    "pricing": {
                      "type": "price_details",
                      "price_details": { "price": "{{linePriceId}}", "product": "prod_test_1" }
                    }
                  }
                ]
              }{{(nextPaymentAttempt.HasValue ? $",\n              \"next_payment_attempt\": {nextPaymentAttempt.Value}" : string.Empty)}}{{(periodEnd.HasValue ? $",\n              \"period_end\": {periodEnd.Value}" : string.Empty)}}
            }
          }
        }
        """;

    private static string MetadataJson(string key, string? userId) =>
        userId is null ? "{}" : $$"""{"{{key}}": "{{userId}}"}""";

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception is not null)
            {
                Messages.Add(exception.ToString());
            }
        }
    }
}
