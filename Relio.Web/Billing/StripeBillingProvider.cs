using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relio.Application.Billing;
using Stripe;
using Stripe.Checkout;
using PlanTier = Relio.Application.Billing.PlanTier;

namespace Relio.Web.Billing;

/// <summary>
/// Live Stripe implementation of <see cref="IBillingProvider"/>, selected by
/// <c>Billing:Provider = Stripe</c>: hosted Checkout to subscribe, the hosted customer portal for
/// self-service changes, and signature-verified webhooks. It only talks to Stripe; the plan state lives
/// in <c>Relio.Data.Billing</c>.
/// </summary>
/// <remarks>
/// <para>
/// The Relio user id travels to Stripe in three places, because each read path sees a different
/// object: <c>client_reference_id</c> and <c>metadata</c> on the Checkout Session, and <c>metadata</c>
/// on the Subscription it creates. Subscription events therefore name their Relio user without an
/// extra API call; when metadata is missing (a subscription created by hand in the Dashboard) the
/// webhook processor matches the stored customer id instead.
/// </para>
/// <para>
/// The tier comes from the subscription's price against the two configured Relio prices, so a price
/// that exists in Stripe but is not configured here never grants Pro.
/// </para>
/// <para>
/// A Stripe account can be shared with other products, and Stripe delivers every event on an account
/// to every endpoint, each signed with that endpoint's own secret. A valid signature therefore does not
/// make an event Relio's. Every event, and every subscription acted on through the API, is ignored
/// unless it carries Relio's own metadata key or bills a configured Relio price;
/// <c>client_reference_id</c> is a generic field every product sets, so it never identifies a Relio
/// user on its own.
/// </para>
/// <para>
/// What Stripe receives: the opaque user id and the account email. Never a name, a person or any other
/// relationship content. Stripe errors are logged by type and code only: their messages can quote
/// submitted values such as an email address.
/// </para>
/// </remarks>
public sealed class StripeBillingProvider : IBillingProvider
{
    /// <summary>The metadata key that marks a Stripe object as Relio's and names its user.</summary>
    public const string UserIdMetadataKey = "relio_user_id";

    /// <summary>Query key the checkout success URL carries the Checkout Session id in.</summary>
    public const string CheckoutSessionIdQueryKey = "session_id";

    /// <summary>Query key and value the checkout cancel URL carries, so the plan page can say checkout was abandoned.</summary>
    public const string CheckoutOutcomeQueryKey = "checkout";

    /// <inheritdoc cref="CheckoutOutcomeQueryKey"/>
    public const string CheckoutCancelledValue = "cancelled";

    private const string CheckoutSessionIdPlaceholder = "{CHECKOUT_SESSION_ID}";
    private const string CheckoutFailedReason = "We couldn't start checkout just now. Please try again in a moment.";
    private const string PortalFailedReason = "We couldn't open the billing portal just now. Please try again in a moment.";

    private static readonly string[] EntitlingStatuses = ["active", "trialing", "past_due"];

    // Statuses in which Stripe will not bill the subscription again on its own. "incomplete" is
    // deliberately absent: it is a first payment still in progress, which either becomes active or
    // expires into "incomplete_expired".
    private static readonly string[] EndedStatuses = ["canceled", "unpaid", "incomplete_expired", "paused"];

    private static readonly TimeSpan DefaultPaymentFailureGracePeriod = TimeSpan.FromDays(7);

    // Added to Stripe's next retry time so the grace period outlasts the retry's own webhook delivery;
    // without it paid access would lapse in the seconds between a retry succeeding and its invoice.paid
    // event arriving.
    private static readonly TimeSpan RetryWebhookAllowance = TimeSpan.FromDays(1);

    private readonly BillingOptions _options;
    private readonly IStripeClient _stripeClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StripeBillingProvider> _logger;

    public StripeBillingProvider(
        IOptions<BillingOptions> options,
        TimeProvider timeProvider,
        ILogger<StripeBillingProvider> logger,
        IStripeClient? stripeClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _stripeClient = stripeClient ?? new StripeClient(_options.ApiKey);
    }

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public async Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        BillingCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);

        var priceId = request.Interval switch
        {
            BillingInterval.Monthly => _options.ProMonthlyPriceId!,
            BillingInterval.Yearly => _options.ProYearlyPriceId!,
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Interval, "Unknown billing interval."),
        };

        var existingCustomerId = string.IsNullOrWhiteSpace(request.ExistingCustomerId) ? null : request.ExistingCustomerId;
        if (existingCustomerId is not null)
        {
            // The stored tier lags Stripe until the webhook lands, so a second tab or a quick double
            // submit would otherwise open a second checkout and a second subscription.
            try
            {
                if (await HasLiveSubscriptionAsync(existingCustomerId, cancellationToken))
                {
                    return CheckoutSessionResult.Unsupported(
                        "You already have a Relio Pro subscription. Use Manage billing to change it.");
                }
            }
            catch (StripeException exception)
            {
                LogStripeFailure(exception, "checking existing subscriptions", request.UserId);
                return CheckoutSessionResult.Unsupported(CheckoutFailedReason);
            }
        }

        var sessionOptions = new SessionCreateOptions
        {
            Mode = "subscription",
            Customer = existingCustomerId,
            // Stripe rejects customer and customer_email together, and an existing customer already
            // carries its own address, so this only seeds the first checkout. Left unset, Checkout
            // collects an address itself (or prefills one from the visitor's Stripe Link account), and
            // the customer ends up under an email unrelated to the Relio account.
            CustomerEmail = existingCustomerId is null && !string.IsNullOrWhiteSpace(request.AccountEmail)
                ? request.AccountEmail
                : null,
            ClientReferenceId = request.UserId,
            Metadata = new Dictionary<string, string> { [UserIdMetadataKey] = request.UserId },
            LineItems = [new SessionLineItemOptions { Price = priceId, Quantity = 1 }],
            AllowPromotionCodes = true,
            // Stripe substitutes {CHECKOUT_SESSION_ID} itself, which lets the plan page confirm the
            // purchase straight away instead of waiting for the webhook.
            SuccessUrl = _options.CheckoutSuccessUrl!.Contains(CheckoutSessionIdPlaceholder, StringComparison.Ordinal)
                ? _options.CheckoutSuccessUrl
                : AppendQuery(_options.CheckoutSuccessUrl, $"{CheckoutSessionIdQueryKey}={CheckoutSessionIdPlaceholder}"),
            CancelUrl = _options.CheckoutCancelUrl!.Contains($"{CheckoutOutcomeQueryKey}=", StringComparison.Ordinal)
                ? _options.CheckoutCancelUrl
                : AppendQuery(_options.CheckoutCancelUrl, $"{CheckoutOutcomeQueryKey}={CheckoutCancelledValue}"),
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string> { [UserIdMetadataKey] = request.UserId },
            },
        };

        try
        {
            var session = await new SessionService(_stripeClient)
                .CreateAsync(sessionOptions, cancellationToken: cancellationToken);

            return string.IsNullOrWhiteSpace(session.Url)
                ? CheckoutSessionResult.Unsupported(CheckoutFailedReason)
                : new CheckoutSessionResult(true, session.Url, null);
        }
        catch (StripeException exception)
        {
            LogStripeFailure(exception, "creating a checkout session", request.UserId);
            return CheckoutSessionResult.Unsupported(CheckoutFailedReason);
        }
    }

    /// <inheritdoc />
    public async Task<ParsedBillingEvent?> GetCompletedCheckoutAsync(
        string userId,
        string checkoutSessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkoutSessionId);

        Session session;
        try
        {
            var options = new SessionGetOptions();
            options.AddExpand("subscription");
            session = await new SessionService(_stripeClient)
                .GetAsync(checkoutSessionId, options, cancellationToken: cancellationToken);
        }
        catch (StripeException exception)
        {
            LogStripeFailure(exception, "reading a checkout session", userId);
            return null;
        }

        if (session.Mode != "subscription" || session.Status != "complete" || session.Subscription is not { } subscription)
        {
            return null;
        }

        // The session id arrives in an address anyone can edit, so it only counts for the user who
        // started it - and only Relio's own metadata says who that is.
        if (GetMetadataUserId(session.Metadata) != userId)
        {
            _logger.LogWarning("A checkout session returned to user {UserId} was not started by them; ignoring it.", userId);
            return null;
        }

        var parsed = MapSubscription(session.Id, "checkout.session.returned", userId, subscription, deleted: false, occurredAtUtc: null);
        return parsed with { BillingProviderCustomerId = session.CustomerId ?? parsed.BillingProviderCustomerId };
    }

    /// <inheritdoc />
    public async Task<PortalSessionResult> CreatePortalSessionAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        try
        {
            var session = await new Stripe.BillingPortal.SessionService(_stripeClient)
                .CreateAsync(
                    new Stripe.BillingPortal.SessionCreateOptions
                    {
                        Customer = customerId,
                        ReturnUrl = _options.PortalReturnUrl,
                    },
                    cancellationToken: cancellationToken);

            return string.IsNullOrWhiteSpace(session.Url)
                ? PortalSessionResult.Unsupported(PortalFailedReason)
                : new PortalSessionResult(true, session.Url, null);
        }
        catch (StripeException exception)
        {
            LogStripeFailure(exception, "creating a portal session", userId: null);
            return PortalSessionResult.Unsupported(PortalFailedReason);
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateCustomerEmailAsync(
        string customerId,
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        try
        {
            await new CustomerService(_stripeClient)
                .UpdateAsync(customerId, new CustomerUpdateOptions { Email = email }, cancellationToken: cancellationToken);
            return true;
        }
        catch (StripeException exception)
        {
            LogStripeFailure(exception, "updating a customer email", userId: null);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<SubscriptionCancellationResult> CancelSubscriptionsAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        var subscriptionService = new SubscriptionService(_stripeClient);
        try
        {
            // Every Relio subscription of the customer, not only the stored one: a duplicate checkout
            // leaves a second subscription Relio never recorded, and it would keep charging. Another
            // product's subscription on the same customer is left alone.
            var subscriptions = await subscriptionService.ListAsync(
                new SubscriptionListOptions { Customer = customerId, Limit = 100 },
                cancellationToken: cancellationToken);

            foreach (var subscription in subscriptions.Data.Where(subscription =>
                subscription.Status is not ("canceled" or "incomplete_expired") && IsRelioSubscription(subscription)))
            {
                try
                {
                    await subscriptionService.CancelAsync(subscription.Id, cancellationToken: cancellationToken);
                }
                catch (StripeException exception) when (exception.StripeError?.Code == "resource_missing")
                {
                    // Already gone on Stripe's side, which is the outcome we wanted.
                }
            }

            return SubscriptionCancellationResult.Success;
        }
        catch (StripeException exception)
        {
            LogStripeFailure(exception, "cancelling subscriptions", userId: null);
            return new SubscriptionCancellationResult(false, "Stripe subscription cancellation failed.");
        }
    }

    /// <inheritdoc />
    public Task<WebhookVerificationResult> VerifyWebhookSignatureAsync(
        string payload,
        string signatureHeader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (string.IsNullOrWhiteSpace(_options.WebhookSigningSecret))
        {
            return Task.FromResult(new WebhookVerificationResult(false, "Webhook signing secret is not configured."));
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return Task.FromResult(new WebhookVerificationResult(false, "Missing webhook signature header."));
        }

        try
        {
            // Also enforces Stripe's default five-minute tolerance on the signed timestamp, so a
            // captured delivery cannot be replayed later.
            EventUtility.ValidateSignature(
                payload,
                signatureHeader,
                _options.WebhookSigningSecret,
                tolerance: 300,
                utcNow: _timeProvider.GetUtcNow().ToUnixTimeSeconds());
            return Task.FromResult(new WebhookVerificationResult(true, null));
        }
        catch (StripeException)
        {
            _logger.LogWarning("Rejected a billing webhook delivery with an invalid Stripe signature.");
            return Task.FromResult(new WebhookVerificationResult(false, "Stripe signature verification failed."));
        }
    }

    /// <inheritdoc />
    public Task<ParsedBillingEvent?> ParseWebhookEventAsync(
        string payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ParseEvent(payload, throwOnApiVersionMismatch: false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Stripe.net throws StripeException, JSON exceptions or InvalidOperationException for a
            // payload that is not an event; all mean "not ours to act on". The type only: a JSON error
            // message can quote the payload.
            _logger.LogWarning(
                "Could not parse a signature-verified Stripe webhook payload ({ExceptionType}).",
                exception.GetType().Name);
            return Task.FromResult<ParsedBillingEvent?>(null);
        }

        var parsed = stripeEvent.Type switch
        {
            EventTypes.CheckoutSessionCompleted => ParseCheckoutCompleted(stripeEvent),
            EventTypes.CustomerSubscriptionCreated
                or EventTypes.CustomerSubscriptionUpdated
                or EventTypes.CustomerSubscriptionDeleted => ParseSubscriptionEvent(stripeEvent),
            EventTypes.InvoicePaymentFailed => ParseInvoicePaymentFailed(stripeEvent),
            EventTypes.InvoicePaid => ParseInvoicePaid(stripeEvent),
            _ => null,
        };
        return Task.FromResult(parsed);
    }

    private ParsedBillingEvent? ParseCheckoutCompleted(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not Session session || session.Mode != "subscription")
        {
            return null;
        }

        // Metadata, not client_reference_id: another product's checkout sets its own user id there.
        var userId = GetMetadataUserId(session.Metadata);
        if (userId is null)
        {
            return null;
        }

        return new ParsedBillingEvent(
            ProviderEventId: stripeEvent.Id,
            EventType: stripeEvent.Type,
            TargetUserId: userId,
            NewTier: null,
            PeriodEndsAtUtc: null,
            BillingProviderCustomerId: session.CustomerId,
            BillingProviderSubscriptionId: session.SubscriptionId,
            OccurredAtUtc: GetOccurredAtUtc(stripeEvent));
    }

    private ParsedBillingEvent? ParseSubscriptionEvent(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not Subscription subscription)
        {
            return null;
        }

        if (!IsRelioSubscription(subscription))
        {
            // Another product's subscription, even when its customer happens to be a Relio customer.
            // Mapping it would downgrade (unknown price -> Free) or relink a paying Relio user.
            return null;
        }

        var parsed = MapSubscription(
            stripeEvent.Id,
            stripeEvent.Type,
            GetMetadataUserId(subscription.Metadata),
            subscription,
            deleted: stripeEvent.Type == EventTypes.CustomerSubscriptionDeleted,
            GetOccurredAtUtc(stripeEvent));
        return parsed with { MayResolveByCustomer = true };
    }

    /// <summary>
    /// Maps a subscription's state onto the plan it entitles its user to. Shared by subscription
    /// webhooks and the checkout-return confirmation, so both apply identical rules.
    /// </summary>
    private ParsedBillingEvent MapSubscription(
        string eventId,
        string eventType,
        string? userId,
        Subscription subscription,
        bool deleted,
        DateTime? occurredAtUtc)
    {
        var linkOnly = new ParsedBillingEvent(
            ProviderEventId: eventId,
            EventType: eventType,
            TargetUserId: userId,
            NewTier: null,
            PeriodEndsAtUtc: null,
            BillingProviderCustomerId: subscription.CustomerId,
            BillingProviderSubscriptionId: subscription.Id,
            OccurredAtUtc: occurredAtUtc);

        if (deleted || EndedStatuses.Contains(subscription.Status))
        {
            return linkOnly with { NewTier = PlanTier.Free, ClearsGracePeriod = true };
        }

        if (!EntitlingStatuses.Contains(subscription.Status))
        {
            return linkOnly;
        }

        var periodEndsAtUtc = GetCurrentPeriodEndUtc(subscription);
        return linkOnly with
        {
            NewTier = ResolveTier(subscription),
            PeriodEndsAtUtc = periodEndsAtUtc,
            ClearsGracePeriod = subscription.Status is "active" or "trialing",
            HasCancellationSchedule = true,
            // The portal schedules a cancellation either as cancel_at_period_end or, on newer API
            // versions, as an explicit cancel_at; both mean "stop renewing on this date".
            CancelsAtUtc = subscription.CancelAt?.ToUniversalTime()
                ?? (subscription.CancelAtPeriodEnd ? periodEndsAtUtc : null),
        };
    }

    private ParsedBillingEvent? ParseInvoicePaymentFailed(Event stripeEvent)
    {
        if (ToInvoiceEvent(stripeEvent) is not { } parsed || stripeEvent.Data.Object is not Invoice invoice)
        {
            return null;
        }

        var gracePeriodEndsAtUtc = invoice.NextPaymentAttempt is { } nextPaymentAttempt
            ? nextPaymentAttempt.ToUniversalTime() + RetryWebhookAllowance
            : _timeProvider.GetUtcNow().UtcDateTime + DefaultPaymentFailureGracePeriod;
        return parsed with { GracePeriodEndsAtUtc = gracePeriodEndsAtUtc };
    }

    private ParsedBillingEvent? ParseInvoicePaid(Event stripeEvent) =>
        // Only the grace period is cleared here. Tier and renewal date come from subscription events
        // alone: an invoice can arrive after the subscription was deleted, and its period end is the
        // period just billed rather than the next renewal.
        ToInvoiceEvent(stripeEvent) is { } parsed ? parsed with { ClearsGracePeriod = true } : null;

    /// <summary>
    /// The common part of an invoice event, or null when it is plainly another product's. Invoices
    /// carry no metadata of their own, so ownership comes from the subscription they bill: its metadata
    /// snapshot, the subscription id Relio stored (resolved by the processor), or a Relio price on one
    /// of the lines. The customer id alone is never enough.
    /// </summary>
    private ParsedBillingEvent? ToInvoiceEvent(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not Invoice invoice)
        {
            return null;
        }

        var subscriptionDetails = invoice.Parent?.SubscriptionDetails;
        var userId = GetMetadataUserId(subscriptionDetails?.Metadata);
        var subscriptionId = subscriptionDetails?.SubscriptionId;
        var billsRelioPrice = invoice.Lines?.Data?
            .Any(line => IsConfiguredPrice(line.Pricing?.PriceDetails?.PriceId)) == true;

        if (userId is null && string.IsNullOrWhiteSpace(subscriptionId) && !billsRelioPrice)
        {
            return null;
        }

        return new ParsedBillingEvent(
            ProviderEventId: stripeEvent.Id,
            EventType: stripeEvent.Type,
            TargetUserId: userId,
            NewTier: null,
            PeriodEndsAtUtc: null,
            BillingProviderSubscriptionId: string.IsNullOrWhiteSpace(subscriptionId) ? null : subscriptionId,
            BillingProviderCustomerId: billsRelioPrice ? invoice.CustomerId : null,
            OccurredAtUtc: GetOccurredAtUtc(stripeEvent),
            MayResolveByCustomer: billsRelioPrice);
    }

    /// <summary>
    /// Whether a subscription was sold by Relio: every Relio checkout stamps its metadata key on the
    /// subscription, and one created by hand in the Dashboard is still recognisable by a Relio price.
    /// </summary>
    private bool IsRelioSubscription(Subscription subscription) =>
        GetMetadataUserId(subscription.Metadata) is not null || GetPriceIds(subscription).Any(IsConfiguredPrice);

    private bool IsConfiguredPrice(string? priceId) =>
        !string.IsNullOrWhiteSpace(priceId)
        && (priceId == _options.ProMonthlyPriceId || priceId == _options.ProYearlyPriceId);

    private static List<string> GetPriceIds(Subscription subscription) =>
        subscription.Items?.Data?
            .Select(item => item.Price?.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList() ?? [];

    private PlanTier ResolveTier(Subscription subscription)
    {
        if (GetPriceIds(subscription).Any(IsConfiguredPrice))
        {
            return PlanTier.Pro;
        }

        _logger.LogWarning(
            "Stripe subscription {SubscriptionId} bills no configured Relio price; treating it as the free plan.",
            subscription.Id);
        return PlanTier.Free;
    }

    private static DateTime? GetCurrentPeriodEndUtc(Subscription subscription)
    {
        var periodEnds = subscription.Items?.Data?
            .Select(item => item.CurrentPeriodEnd)
            .Where(end => end != default)
            .ToList();

        return periodEnds is { Count: > 0 } ? periodEnds.Max().ToUniversalTime() : null;
    }

    private static DateTime? GetOccurredAtUtc(Event stripeEvent) =>
        stripeEvent.Created == default ? null : stripeEvent.Created.ToUniversalTime();

    private static string? GetMetadataUserId(IDictionary<string, string>? metadata) =>
        metadata is not null && metadata.TryGetValue(UserIdMetadataKey, out var userId) && !string.IsNullOrWhiteSpace(userId)
            ? userId
            : null;

    private async Task<bool> HasLiveSubscriptionAsync(string customerId, CancellationToken cancellationToken)
    {
        var subscriptions = await new SubscriptionService(_stripeClient).ListAsync(
            new SubscriptionListOptions { Customer = customerId, Limit = 100 },
            cancellationToken: cancellationToken);

        return subscriptions.Data.Any(subscription =>
            EntitlingStatuses.Contains(subscription.Status) && IsRelioSubscription(subscription));
    }

    private void LogStripeFailure(StripeException exception, string operation, string? userId) =>
        // Type, HTTP status and Stripe's error code only: Stripe's messages can quote submitted
        // values (an email address), which must never reach the logs.
        _logger.LogError(
            "Stripe failed while {Operation} for user {UserId} ({ExceptionType}, HTTP {StatusCode}, code {StripeErrorCode}).",
            operation,
            userId,
            exception.GetType().Name,
            (int)exception.HttpStatusCode,
            exception.StripeError?.Code);

    private static string AppendQuery(string url, string query) =>
        url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + query;
}
