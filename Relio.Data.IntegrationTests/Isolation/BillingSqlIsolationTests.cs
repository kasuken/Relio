using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relio.Application.Accounts;
using Relio.Application.Billing;
using Relio.Data.Accounts;
using Relio.Data.Billing;
using Relio.Data.Identity;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.People;
using Relio.Application.People;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Isolation;

/// <summary>
/// Hosted billing against SQL Server: the plan is per owner, webhooks are a signed capability, the
/// ledger's unique index absorbs a concurrent duplicate, the limit query translates, and erasure
/// removes the subscription row in the right order for its NO ACTION foreign key.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class BillingSqlIsolationTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Subscription_service_reads_and_writes_only_the_signed_in_owners_plan()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        var customerA = $"cus_{Guid.NewGuid():N}";
        await using (var setup = fixture.CreateDbContext())
        {
            setup.UserSubscriptions.Add(new UserSubscription
            {
                UserId = harness.OwnerA.Id,
                Tier = PlanTier.Pro,
                BillingProviderCustomerId = customerA,
                BillingProviderSubscriptionId = $"sub_{Guid.NewGuid():N}",
            });
            await setup.SaveChangesAsync();
        }

        var provider = new RecordingBillingProvider();
        await using (var ownerB = harness.AsOwnerB())
        {
            var service = new SubscriptionService(ownerB.DbContext, ownerB.CurrentUser, provider, harness.Clock);

            var summary = await service.GetSummaryAsync();
            summary.Tier.Should().Be(PlanTier.Free);
            summary.HasBillingAccount.Should().BeFalse();
            (await service.OpenPortalAsync()).Supported.Should().BeFalse();
            (await service.StartCheckoutAsync(BillingInterval.Yearly)).Supported.Should().BeTrue();
            provider.CheckoutRequests.Should().ContainSingle()
                .Which.Should().Be(new BillingCheckoutRequest(harness.OwnerB.Id, BillingInterval.Yearly, null, harness.OwnerB.Email));

            provider.CompletedCheckout = new ParsedBillingEvent(
                $"cs_test_{Guid.NewGuid():N}", "checkout.session.returned", harness.OwnerB.Id, PlanTier.Pro,
                harness.Clock.GetUtcNow().UtcDateTime.AddMonths(1),
                BillingProviderCustomerId: $"cus_{Guid.NewGuid():N}", ClearsGracePeriod: true);
            (await service.ConfirmCheckoutAsync("cs_test_owner_b")).Should().BeTrue();
            ownerB.DbContext.ChangeTracker.Entries().Should().BeEmpty();
        }

        await using (var ownerA = harness.AsOwnerA())
        {
            var service = new SubscriptionService(ownerA.DbContext, ownerA.CurrentUser, provider, harness.Clock);

            (await service.StartCheckoutAsync(BillingInterval.Monthly)).UnsupportedReason
                .Should().Be(SubscriptionService.AlreadySubscribedReason);
            (await service.OpenPortalAsync()).Supported.Should().BeTrue();
            provider.PortalCustomers.Should().Equal(customerA);
        }

        await using var verify = fixture.CreateDbContext();
        var rows = await verify.UserSubscriptions.AsNoTracking()
            .Where(row => row.UserId == harness.OwnerA.Id || row.UserId == harness.OwnerB.Id)
            .ToListAsync();
        rows.Should().HaveCount(2);
        rows.Single(row => row.UserId == harness.OwnerA.Id).BillingProviderCustomerId.Should().Be(customerA);
        rows.Single(row => row.UserId == harness.OwnerB.Id).Tier.Should().Be(PlanTier.Pro);
    }

    [SqlServerFact]
    public async Task Webhook_processor_applies_signed_events_to_the_named_owner_only_and_rejects_unsigned_ones()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        var eventId = $"evt_{Guid.NewGuid():N}";
        var provider = new RecordingBillingProvider
        {
            WebhookEvent = new ParsedBillingEvent(
                eventId, "customer.subscription.updated", harness.OwnerA.Id, PlanTier.Pro,
                harness.Clock.GetUtcNow().UtcDateTime.AddYears(1),
                BillingProviderCustomerId: $"cus_{Guid.NewGuid():N}",
                BillingProviderSubscriptionId: $"sub_{Guid.NewGuid():N}",
                ClearsGracePeriod: true,
                OccurredAtUtc: harness.Clock.GetUtcNow().UtcDateTime,
                HasCancellationSchedule: true,
                MayResolveByCustomer: true),
        };

        await using (var unsigned = fixture.CreateDbContext())
        {
            provider.SignatureIsValid = false;
            var rejected = await CreateProcessor(unsigned, provider, harness.Clock).ProcessAsync("{}", "t=1,v1=forged");
            rejected.Should().Be(BillingWebhookProcessingResult.InvalidSignature);
        }

        provider.SignatureIsValid = true;
        await using (var signed = fixture.CreateDbContext())
        {
            (await CreateProcessor(signed, provider, harness.Clock).ProcessAsync("{}", "sig"))
                .Should().Be(BillingWebhookProcessingResult.Applied);
            (await CreateProcessor(signed, provider, harness.Clock).ProcessAsync("{}", "sig"))
                .Should().Be(BillingWebhookProcessingResult.AlreadyProcessed);
        }

        await using var verify = fixture.CreateDbContext();
        (await verify.UserSubscriptions.AsNoTracking().SingleAsync(row => row.UserId == harness.OwnerA.Id))
            .Tier.Should().Be(PlanTier.Pro);
        (await verify.UserSubscriptions.AsNoTracking().AnyAsync(row => row.UserId == harness.OwnerB.Id))
            .Should().BeFalse();
        (await verify.ProcessedBillingEvents.AsNoTracking().CountAsync(row => row.ProviderEventId == eventId))
            .Should().Be(1);
    }

    [SqlServerFact]
    public async Task A_concurrent_duplicate_delivery_loses_on_the_unique_event_index_and_changes_nothing()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        var eventId = $"evt_{Guid.NewGuid():N}";
        var provider = new RecordingBillingProvider
        {
            WebhookEvent = new ParsedBillingEvent(
                eventId, "customer.subscription.updated", harness.OwnerA.Id, PlanTier.Pro, null,
                OccurredAtUtc: harness.Clock.GetUtcNow().UtcDateTime),
        };

        // The other delivery records the same event between this one's check and its save.
        var interceptor = new RunOnFirstSaveInterceptor(async cancellationToken =>
        {
            await using var winner = fixture.CreateDbContext();
            winner.ProcessedBillingEvents.Add(new ProcessedBillingEvent
            {
                ProviderEventId = eventId,
                EventType = "customer.subscription.updated",
                ProcessedAtUtc = harness.Clock.GetUtcNow().UtcDateTime,
            });
            await winner.SaveChangesAsync(cancellationToken);
        });
        await using var loser = fixture.CreateDbContext(interceptor);

        var result = await CreateProcessor(loser, provider, harness.Clock).ProcessAsync("{}", "sig");

        result.Should().Be(BillingWebhookProcessingResult.AlreadyProcessed);
        interceptor.Fired.Should().BeTrue();
        await using var verify = fixture.CreateDbContext();
        (await verify.UserSubscriptions.AsNoTracking().AnyAsync(row => row.UserId == harness.OwnerA.Id))
            .Should().BeFalse("the losing save rolled back its plan change with its ledger row");
        (await verify.ProcessedBillingEvents.AsNoTracking().CountAsync(row => row.ProviderEventId == eventId))
            .Should().Be(1);
    }

    [SqlServerFact]
    public async Task Email_sync_updates_only_the_named_users_billing_customer()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        var customerA = $"cus_{Guid.NewGuid():N}";
        var customerB = $"cus_{Guid.NewGuid():N}";
        await using (var setup = fixture.CreateDbContext())
        {
            setup.UserSubscriptions.AddRange(
                new UserSubscription { UserId = harness.OwnerA.Id, BillingProviderCustomerId = customerA },
                new UserSubscription { UserId = harness.OwnerB.Id, BillingProviderCustomerId = customerB });
            await setup.SaveChangesAsync();
        }

        var provider = new RecordingBillingProvider();
        await using var dbContext = fixture.CreateDbContext();

        (await new BillingCustomerEmailSync(dbContext, provider).SyncAsync(harness.OwnerA.Id, "new-a@example.com"))
            .Should().BeTrue();

        provider.EmailUpdates.Should().Equal((customerA, "new-a@example.com"));
    }

    [SqlServerFact]
    public async Task The_free_plan_limit_query_counts_only_the_owners_active_people_on_sql_server()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        await using (var setup = fixture.CreateDbContext())
        {
            for (var index = 0; index < PlanCatalog.FreeActivePeopleLimit; index++)
            {
                setup.People.Add(new Person { OwnerId = harness.OwnerA.Id, FirstName = $"Synthetic {index}" });
            }

            setup.People.Add(new Person { OwnerId = harness.OwnerA.Id, FirstName = "Archived", IsArchived = true });
            setup.People.Add(new Person { OwnerId = harness.OwnerB.Id, FirstName = "Other owner" });
            await setup.SaveChangesAsync();
        }

        var limits = new PlanLimits(new RecordingBillingProvider());
        await using (var ownerA = harness.AsOwnerA())
        {
            var act = () => new PeopleService(ownerA.DbContext, ownerA.CurrentUser, harness.Clock, limits)
                .CreateAsync(new CreatePersonRequest { FirstName = "Over the limit" });
            await act.Should().ThrowAsync<PlanLimitReachedException>();
        }

        await using (var ownerB = harness.AsOwnerB())
        {
            await new PeopleService(ownerB.DbContext, ownerB.CurrentUser, harness.Clock, limits)
                .CreateAsync(new CreatePersonRequest { FirstName = "Still room" });
        }

        await using var verify = fixture.CreateDbContext();
        (await verify.People.AsNoTracking().CountAsync(person => person.OwnerId == harness.OwnerA.Id))
            .Should().Be(PlanCatalog.FreeActivePeopleLimit + 1);
    }

    [SqlServerFact]
    public async Task Erasure_cancels_the_subscription_and_deletes_its_row_before_the_identity_owner()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        var customerA = $"cus_{Guid.NewGuid():N}";
        await using (var setup = fixture.CreateDbContext())
        {
            setup.UserSubscriptions.Add(new UserSubscription
            {
                UserId = harness.OwnerA.Id,
                Tier = PlanTier.Pro,
                BillingProviderCustomerId = customerA,
            });
            setup.UserSubscriptions.Add(new UserSubscription { UserId = harness.OwnerB.Id, Tier = PlanTier.Pro });
            await setup.SaveChangesAsync();
        }

        var provider = new RecordingBillingProvider();
        await using (var ownerA = harness.AsOwnerA())
        {
            var result = await new AccountDeletionService(
                    ownerA.DbContext,
                    ownerA.CurrentUser,
                    new PasswordHasher<RelioUser>(),
                    NullLogger<AccountDeletionService>.Instance,
                    provider)
                .DeleteAsync(new AccountDeletionRequest(SqlIsolationTestHarness.TestPassword, Confirmed: true));

            result.Status.Should().Be(AccountDeletionStatus.Deleted);
        }

        provider.CancelledCustomers.Should().Equal(customerA);
        await using var verify = fixture.CreateDbContext();
        (await verify.Users.AsNoTracking().AnyAsync(user => user.Id == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.UserSubscriptions.AsNoTracking().AnyAsync(row => row.UserId == harness.OwnerA.Id)).Should().BeFalse();
        (await verify.UserSubscriptions.AsNoTracking().AnyAsync(row => row.UserId == harness.OwnerB.Id)).Should().BeTrue();
    }

    private static BillingWebhookProcessor CreateProcessor(
        RelioDbContext dbContext,
        IBillingProvider provider,
        TimeProvider clock) =>
        new(dbContext, provider, clock, NullLogger<BillingWebhookProcessor>.Instance);

    /// <summary>An enabled provider that never calls Stripe and records what would have been sent.</summary>
    private sealed class RecordingBillingProvider : IBillingProvider
    {
        public bool IsEnabled => true;

        public bool SignatureIsValid { get; set; } = true;

        public ParsedBillingEvent? WebhookEvent { get; set; }

        public ParsedBillingEvent? CompletedCheckout { get; set; }

        public List<BillingCheckoutRequest> CheckoutRequests { get; } = [];

        public List<string> PortalCustomers { get; } = [];

        public List<string> CancelledCustomers { get; } = [];

        public List<(string CustomerId, string Email)> EmailUpdates { get; } = [];

        public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(BillingCheckoutRequest request, CancellationToken cancellationToken = default)
        {
            CheckoutRequests.Add(request);
            return Task.FromResult(new CheckoutSessionResult(true, "https://checkout.stripe.com/c/pay/cs_test_fake", null));
        }

        public Task<ParsedBillingEvent?> GetCompletedCheckoutAsync(string userId, string checkoutSessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(CompletedCheckout?.TargetUserId == userId ? CompletedCheckout : null);

        public Task<PortalSessionResult> CreatePortalSessionAsync(string customerId, CancellationToken cancellationToken = default)
        {
            PortalCustomers.Add(customerId);
            return Task.FromResult(new PortalSessionResult(true, "https://billing.stripe.com/p/session/test_fake", null));
        }

        public Task<bool> UpdateCustomerEmailAsync(string customerId, string email, CancellationToken cancellationToken = default)
        {
            EmailUpdates.Add((customerId, email));
            return Task.FromResult(true);
        }

        public Task<SubscriptionCancellationResult> CancelSubscriptionsAsync(string customerId, CancellationToken cancellationToken = default)
        {
            CancelledCustomers.Add(customerId);
            return Task.FromResult(SubscriptionCancellationResult.Success);
        }

        public Task<WebhookVerificationResult> VerifyWebhookSignatureAsync(string payload, string signatureHeader, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WebhookVerificationResult(SignatureIsValid, null));

        public Task<ParsedBillingEvent?> ParseWebhookEventAsync(string payload, CancellationToken cancellationToken = default) =>
            Task.FromResult(WebhookEvent);
    }
}
