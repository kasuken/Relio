using Microsoft.EntityFrameworkCore;
using Relio.Application.Billing;
using Relio.Application.Security;

namespace Relio.Data.Billing;

/// <summary>
/// EF-backed <see cref="ISubscriptionService"/>: the signed-in user's plan, checkout and portal.
/// </summary>
/// <remarks>
/// Reads are untracked and the one write (<see cref="ConfirmCheckoutAsync"/>) saves once and clears
/// the change tracker, like every data service on a circuit-lived context. Only the user id and the
/// account email reach the provider.
/// </remarks>
public sealed class SubscriptionService(
    RelioDbContext dbContext,
    ICurrentUser currentUser,
    IBillingProvider billingProvider,
    TimeProvider timeProvider) : ISubscriptionService
{
    /// <summary>Shown when someone who already pays tries to check out again.</summary>
    public const string AlreadySubscribedReason =
        "You already have a Relio Pro subscription. Use Manage billing to change it.";

    /// <summary>Shown when someone without a billing customer opens the portal.</summary>
    public const string NoBillingAccountReason =
        "You don't have a billing account yet. Subscribe to Relio Pro first.";

    // Stripe Checkout Session ids are "cs_test_..." or "cs_live_..."; anything else in the editable
    // query string is never sent to the provider.
    private const int MaxCheckoutSessionIdLength = 255;

    /// <inheritdoc />
    public async Task<PlanSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var activePeople = await dbContext.People
            .AsNoTracking()
            .CountAsync(person => person.OwnerId == ownerId && !person.IsArchived, cancellationToken);

        if (!billingProvider.IsEnabled)
        {
            return new PlanSummary(
                BillingEnabled: false,
                Tier: PlanTier.Free,
                ActivePeople: activePeople,
                MaxActivePeople: null,
                PlanRenewsAtUtc: null,
                PlanCancelsAtUtc: null,
                GracePeriodEndsAtUtc: null,
                PaidAccessSuspended: false,
                HasBillingAccount: false);
        }

        var stored = await dbContext.UserSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(subscription => subscription.UserId == ownerId, cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var storedTier = stored?.Tier ?? PlanTier.Free;
        var suspended = SubscriptionStateRules.IsPaidAccessSuspended(storedTier, stored?.GracePeriodEndsAtUtc, nowUtc);
        var tier = suspended ? PlanTier.Free : storedTier;

        return new PlanSummary(
            BillingEnabled: true,
            Tier: tier,
            ActivePeople: activePeople,
            MaxActivePeople: PlanCatalog.Get(tier).MaxActivePeople,
            PlanRenewsAtUtc: stored?.PlanRenewsAtUtc,
            PlanCancelsAtUtc: stored?.PlanCancelsAtUtc,
            GracePeriodEndsAtUtc: stored?.GracePeriodEndsAtUtc,
            PaidAccessSuspended: suspended,
            HasBillingAccount: !string.IsNullOrEmpty(stored?.BillingProviderCustomerId));
    }

    /// <inheritdoc />
    public async Task<CheckoutSessionResult> StartCheckoutAsync(
        BillingInterval interval,
        CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();
        if (!Enum.IsDefined(interval))
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Unknown billing interval.");
        }

        if (!billingProvider.IsEnabled)
        {
            return CheckoutSessionResult.Unsupported(NullBillingProvider.NotConfiguredReason);
        }

        var stored = await dbContext.UserSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == ownerId)
            .Select(subscription => new { subscription.Tier, subscription.BillingProviderCustomerId })
            .SingleOrDefaultAsync(cancellationToken);

        // Also while paid access is suspended: that subscription still exists at the provider and is
        // fixed in the portal; a second checkout would start a second subscription.
        if (stored?.Tier == PlanTier.Pro)
        {
            return CheckoutSessionResult.Unsupported(AlreadySubscribedReason);
        }

        var email = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == ownerId)
            .Select(user => user.Email)
            .SingleOrDefaultAsync(cancellationToken);

        return await billingProvider.CreateCheckoutSessionAsync(
            new BillingCheckoutRequest(ownerId, interval, stored?.BillingProviderCustomerId, email),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PortalSessionResult> OpenPortalAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        if (!billingProvider.IsEnabled)
        {
            return PortalSessionResult.Unsupported(NullBillingProvider.NotConfiguredReason);
        }

        var customerId = await dbContext.UserSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == ownerId)
            .Select(subscription => subscription.BillingProviderCustomerId)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrEmpty(customerId))
        {
            return PortalSessionResult.Unsupported(NoBillingAccountReason);
        }

        return await billingProvider.CreatePortalSessionAsync(customerId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmCheckoutAsync(string checkoutSessionId, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        if (!billingProvider.IsEnabled
            || string.IsNullOrWhiteSpace(checkoutSessionId)
            || checkoutSessionId.Length > MaxCheckoutSessionIdLength
            || !checkoutSessionId.StartsWith("cs_", StringComparison.Ordinal))
        {
            return false;
        }

        // The provider checks the session was started by this user: the id arrives in an address
        // anyone can edit.
        var completed = await billingProvider.GetCompletedCheckoutAsync(ownerId, checkoutSessionId, cancellationToken);
        if (completed is null)
        {
            return false;
        }

        try
        {
            // Read live rather than delivered, so it has no event time and is not written to the
            // webhook ledger: the checkout's own webhooks still arrive and re-apply the same state.
            var subscription = await SubscriptionStore.GetOrAddTrackedAsync(dbContext, ownerId, cancellationToken);
            SubscriptionStateRules.Apply(subscription, completed);
            await dbContext.SaveChangesAsync(cancellationToken);

            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            return SubscriptionStateRules.EffectiveTier(subscription.Tier, subscription.GracePeriodEndsAtUtc, nowUtc)
                == PlanTier.Pro;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }
}
