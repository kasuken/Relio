using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Billing;
using Relio.Application.Security;
using Relio.Data.Billing;
using Relio.Data.Tests.People;
using static Relio.Data.Tests.Billing.BillingTestHarness;

namespace Relio.Data.Tests.Billing;

public sealed class SubscriptionServiceTests
{
    private const string UserId = "plan-user";
    private const string OtherUserId = "plan-other";

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeBillingProvider _provider = new();

    [Fact]
    public async Task Every_method_requires_a_signed_in_user()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        var service = CreateService(dbContext, userId: null);

        await FluentActions.Awaiting(() => service.GetSummaryAsync()).Should().ThrowAsync<UnauthenticatedUserException>();
        await FluentActions.Awaiting(() => service.StartCheckoutAsync(BillingInterval.Yearly)).Should().ThrowAsync<UnauthenticatedUserException>();
        await FluentActions.Awaiting(() => service.OpenPortalAsync()).Should().ThrowAsync<UnauthenticatedUserException>();
        await FluentActions.Awaiting(() => service.ConfirmCheckoutAsync("cs_test_1")).Should().ThrowAsync<UnauthenticatedUserException>();
        _provider.CheckoutRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_billing_there_is_no_plan_and_no_limit()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddPeopleAsync(dbContext, UserId, active: 30);
        _provider.IsEnabled = false;

        var summary = await CreateService(dbContext, UserId).GetSummaryAsync();
        var checkout = await CreateService(dbContext, UserId).StartCheckoutAsync(BillingInterval.Monthly);

        summary.BillingEnabled.Should().BeFalse();
        summary.MaxActivePeople.Should().BeNull();
        summary.LimitReached.Should().BeFalse();
        checkout.Supported.Should().BeFalse();
        _provider.CheckoutRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_new_account_is_on_the_free_plan_and_archived_people_do_not_count()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddPeopleAsync(dbContext, UserId, active: PlanCatalog.FreeActivePeopleLimit, archived: 3);

        var summary = await CreateService(dbContext, UserId).GetSummaryAsync();

        summary.BillingEnabled.Should().BeTrue();
        summary.Tier.Should().Be(PlanTier.Free);
        summary.ActivePeople.Should().Be(PlanCatalog.FreeActivePeopleLimit);
        summary.MaxActivePeople.Should().Be(PlanCatalog.FreeActivePeopleLimit);
        summary.LimitReached.Should().BeTrue();
        summary.HasBillingAccount.Should().BeFalse();
    }

    [Fact]
    public async Task Pro_is_unlimited_until_its_grace_period_after_a_failed_payment_passes()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddSubscriptionAsync(dbContext, Pro(UserId, graceEndsAtUtc: Now.UtcDateTime.AddDays(2)));

        var during = await CreateService(dbContext, UserId).GetSummaryAsync();
        _clock.Advance(TimeSpan.FromDays(3));
        var after = await CreateService(dbContext, UserId).GetSummaryAsync();

        during.Tier.Should().Be(PlanTier.Pro);
        during.MaxActivePeople.Should().BeNull();
        during.PaidAccessSuspended.Should().BeFalse();
        after.Tier.Should().Be(PlanTier.Free);
        after.PaidAccessSuspended.Should().BeTrue();
        after.MaxActivePeople.Should().Be(PlanCatalog.FreeActivePeopleLimit);
    }

    [Fact]
    public async Task Checkout_sends_only_the_user_id_the_existing_customer_and_the_account_email()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddUserAsync(dbContext, UserId, "plan-user@example.com");
        await AddSubscriptionAsync(dbContext, new UserSubscription { UserId = UserId, BillingProviderCustomerId = "cus_earlier" });

        var result = await CreateService(dbContext, UserId).StartCheckoutAsync(BillingInterval.Monthly);

        result.Supported.Should().BeTrue();
        _provider.CheckoutRequests.Should().ContainSingle()
            .Which.Should().Be(new BillingCheckoutRequest(UserId, BillingInterval.Monthly, "cus_earlier", "plan-user@example.com"));
    }

    [Fact]
    public async Task Checkout_is_refused_while_a_subscription_exists_even_when_suspended()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddSubscriptionAsync(dbContext, Pro(UserId, graceEndsAtUtc: Now.UtcDateTime.AddDays(-1)));

        var result = await CreateService(dbContext, UserId).StartCheckoutAsync(BillingInterval.Yearly);

        result.Supported.Should().BeFalse();
        result.UnsupportedReason.Should().Be(SubscriptionService.AlreadySubscribedReason);
        _provider.CheckoutRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Checkout_rejects_an_undefined_interval()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);

        var act = () => CreateService(dbContext, UserId).StartCheckoutAsync((BillingInterval)9);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task The_portal_opens_for_the_users_own_customer_only()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddSubscriptionAsync(dbContext, Pro(UserId, customerId: "cus_mine"));

        var mine = await CreateService(dbContext, UserId).OpenPortalAsync();
        var other = await CreateService(dbContext, OtherUserId).OpenPortalAsync();

        mine.Supported.Should().BeTrue();
        other.Supported.Should().BeFalse();
        other.UnsupportedReason.Should().Be(SubscriptionService.NoBillingAccountReason);
        _provider.PortalCustomers.Should().Equal("cus_mine");
    }

    [Fact]
    public async Task Another_users_subscription_never_shows_in_my_summary()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        await AddSubscriptionAsync(dbContext, Pro(OtherUserId));
        await AddPeopleAsync(dbContext, OtherUserId, active: 4);

        var summary = await CreateService(dbContext, UserId).GetSummaryAsync();

        summary.Tier.Should().Be(PlanTier.Free);
        summary.ActivePeople.Should().Be(0);
        summary.HasBillingAccount.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("evt_not_a_session")]
    [InlineData("cs_<script>")]
    public async Task A_malformed_checkout_id_is_never_sent_to_the_provider(string sessionId)
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);

        var confirmed = await CreateService(dbContext, UserId).ConfirmCheckoutAsync(sessionId);

        // "cs_<script>" is passed on (the provider owns the lookup) but answers nothing.
        confirmed.Should().BeFalse();
        _provider.CheckoutLookups.Should().OnlyContain(lookup => lookup.SessionId.StartsWith("cs_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_completed_checkout_is_applied_at_once_for_the_current_user()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        _provider.CompletedCheckout = ProEvent("cs_test_done", UserId, occurredAtUtc: null) with { OccurredAtUtc = null };

        var confirmed = await CreateService(dbContext, UserId).ConfirmCheckoutAsync("cs_test_done");

        confirmed.Should().BeTrue();
        _provider.CheckoutLookups.Should().Equal((UserId, "cs_test_done"));
        var stored = await ReadSubscriptionAsync(dbContext, UserId);
        stored!.Tier.Should().Be(PlanTier.Pro);
        stored.LastBillingEventAtUtc.Should().BeNull("a live read is not a delivered event");
        (await dbContext.ProcessedBillingEvents.AnyAsync()).Should().BeFalse();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_or_someone_elses_checkout_changes_nothing()
    {
        await using var dbContext = CreateDbContext(NewDatabase(), _clock);
        _provider.CompletedCheckout = null;

        var confirmed = await CreateService(dbContext, UserId).ConfirmCheckoutAsync("cs_test_someone_else");

        confirmed.Should().BeFalse();
        (await ReadSubscriptionAsync(dbContext, UserId)).Should().BeNull();
    }

    private SubscriptionService CreateService(RelioDbContext dbContext, string? userId = UserId) =>
        new(dbContext, new FakeCurrentUser(userId), _provider, _clock);
}
