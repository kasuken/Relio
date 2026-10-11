using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Relio.Application.Billing;
using Relio.Data.Configurations;

namespace Relio.Data.Billing;

/// <summary>
/// EF-backed <see cref="IBillingWebhookProcessor"/>. Under <see cref="NullBillingProvider"/> every
/// signature check fails, so no unauthenticated request can change anyone's plan without a real
/// provider configured.
/// </summary>
/// <remarks>
/// <para>
/// <b>One save.</b> The plan change and the ledger row that marks the event processed are written in
/// the same <c>SaveChangesAsync</c> (one transaction on SQL Server), so a failure leaves neither and the
/// provider's retry applies the event again; there is no window in which an event is recorded but its
/// change lost. A concurrent duplicate delivery loses on the unique event-id index and is acknowledged.
/// </para>
/// <para>
/// <b>Which user.</b> The provider's own metadata names the Relio user when Relio created the checkout.
/// Otherwise the stored subscription id, then (only for events known to be a Relio subscription's) the
/// stored customer id: on a Stripe account shared with other products, the same customer can have
/// another product's subscriptions too.
/// </para>
/// <para>Logs event types, outcomes and ids only - never an email or any payload.</para>
/// </remarks>
public sealed class BillingWebhookProcessor(
    RelioDbContext dbContext,
    IBillingProvider billingProvider,
    TimeProvider timeProvider,
    ILogger<BillingWebhookProcessor> logger) : IBillingWebhookProcessor
{
    private const int MaxEventTypeLength = 100;

    /// <inheritdoc />
    public async Task<BillingWebhookProcessingResult> ProcessAsync(
        string payload,
        string signatureHeader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(signatureHeader);

        var verification = await billingProvider.VerifyWebhookSignatureAsync(payload, signatureHeader, cancellationToken);
        if (!verification.IsValid)
        {
            return BillingWebhookProcessingResult.InvalidSignature;
        }

        var billingEvent = await billingProvider.ParseWebhookEventAsync(payload, cancellationToken);
        if (billingEvent is null)
        {
            return BillingWebhookProcessingResult.Ignored;
        }

        dbContext.ChangeTracker.Clear();
        try
        {
            var alreadyProcessed = await dbContext.ProcessedBillingEvents
                .AsNoTracking()
                .AnyAsync(row => row.ProviderEventId == billingEvent.ProviderEventId, cancellationToken);
            if (alreadyProcessed)
            {
                return BillingWebhookProcessingResult.AlreadyProcessed;
            }

            var userId = await ResolveUserIdAsync(billingEvent, cancellationToken);
            BillingWebhookProcessingResult outcome;
            if (userId is null
                || !await dbContext.Users.AsNoTracking().AnyAsync(user => user.Id == userId, cancellationToken))
            {
                // Another product's event that slipped through, or the subscription.deleted that an
                // account's own erasure triggered: recorded and acknowledged, so it is not retried.
                outcome = BillingWebhookProcessingResult.UnknownUser;
            }
            else
            {
                var subscription = await SubscriptionStore.GetOrAddTrackedAsync(dbContext, userId, cancellationToken);
                outcome = SubscriptionStateRules.Apply(subscription, billingEvent)
                    ? BillingWebhookProcessingResult.Applied
                    : BillingWebhookProcessingResult.StaleEvent;
            }

            dbContext.ProcessedBillingEvents.Add(new ProcessedBillingEvent
            {
                ProviderEventId = billingEvent.ProviderEventId,
                EventType = billingEvent.EventType.Length > MaxEventTypeLength
                    ? billingEvent.EventType[..MaxEventTypeLength]
                    : billingEvent.EventType,
                ProcessedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            });

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
                when (SqlServerErrors.IsUniqueIndexViolation(exception, ProcessedBillingEventConfiguration.ProviderEventIdIndexName))
            {
                // A concurrent delivery of the same event won the race and applied the same change.
                return BillingWebhookProcessingResult.AlreadyProcessed;
            }

            logger.LogInformation(
                "Billing event {EventType} for user {UserId}: {Outcome}.",
                billingEvent.EventType,
                outcome == BillingWebhookProcessingResult.UnknownUser ? null : userId,
                outcome.Reason);
            return outcome;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    private async Task<string?> ResolveUserIdAsync(ParsedBillingEvent billingEvent, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(billingEvent.TargetUserId))
        {
            return billingEvent.TargetUserId;
        }

        if (!string.IsNullOrWhiteSpace(billingEvent.BillingProviderSubscriptionId))
        {
            var bySubscription = await dbContext.UserSubscriptions
                .AsNoTracking()
                .Where(row => row.BillingProviderSubscriptionId == billingEvent.BillingProviderSubscriptionId)
                .Select(row => row.UserId)
                .FirstOrDefaultAsync(cancellationToken);
            if (bySubscription is not null)
            {
                return bySubscription;
            }
        }

        if (billingEvent.MayResolveByCustomer && !string.IsNullOrWhiteSpace(billingEvent.BillingProviderCustomerId))
        {
            return await dbContext.UserSubscriptions
                .AsNoTracking()
                .Where(row => row.BillingProviderCustomerId == billingEvent.BillingProviderCustomerId)
                .Select(row => row.UserId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return null;
    }
}
