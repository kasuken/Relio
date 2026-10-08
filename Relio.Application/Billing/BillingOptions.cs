namespace Relio.Application.Billing;

/// <summary>
/// Configuration options for the Relio billing subsystem (<c>Billing</c> section).
/// </summary>
public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>
    /// The billing provider to use. Defaults to <see cref="BillingProviderType.None"/>.
    /// </summary>
    public BillingProviderType Provider { get; set; } = BillingProviderType.None;

    /// <summary>
    /// Stripe Secret API Key (e.g. <c>sk_test_...</c> or <c>sk_live_...</c>).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Stripe Publishable Key (e.g. <c>pk_test_...</c> or <c>pk_live_...</c>).
    /// </summary>
    public string? PublishableKey { get; set; }

    /// <summary>
    /// Stripe Webhook Signing Secret (e.g. <c>whsec_...</c>).
    /// </summary>
    public string? WebhookSigningSecret { get; set; }

    /// <summary>
    /// Stripe Price ID for monthly subscription.
    /// </summary>
    public string? ProMonthlyPriceId { get; set; }

    /// <summary>
    /// Stripe Price ID for yearly subscription.
    /// </summary>
    public string? ProYearlyPriceId { get; set; }

    /// <summary>
    /// URL to redirect the user to after successful checkout.
    /// </summary>
    public string? CheckoutSuccessUrl { get; set; }

    /// <summary>
    /// URL to redirect the user to after cancelled checkout.
    /// </summary>
    public string? CheckoutCancelUrl { get; set; }

    /// <summary>
    /// Return URL after the customer portal session.
    /// </summary>
    public string? PortalReturnUrl { get; set; }

    /// <summary>
    /// Checks whether all required Stripe configuration properties are supplied.
    /// </summary>
    public bool IsStripeFullyConfigured =>
        Provider == BillingProviderType.Stripe
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(WebhookSigningSecret)
        && !string.IsNullOrWhiteSpace(ProMonthlyPriceId)
        && !string.IsNullOrWhiteSpace(ProYearlyPriceId);

    /// <summary>
    /// Validates Stripe configuration options, throwing <see cref="InvalidOperationException"/>
    /// naming all missing settings if <see cref="Provider"/> is <see cref="BillingProviderType.Stripe"/>.
    /// </summary>
    public void Validate()
    {
        if (Provider != BillingProviderType.Stripe)
        {
            return;
        }

        var missing = new List<string>();
        void Require(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                missing.Add($"{SectionName}:{name}");
            }
        }

        Require(ApiKey, nameof(ApiKey));
        Require(WebhookSigningSecret, nameof(WebhookSigningSecret));
        Require(ProMonthlyPriceId, nameof(ProMonthlyPriceId));
        Require(ProYearlyPriceId, nameof(ProYearlyPriceId));
        Require(CheckoutSuccessUrl, nameof(CheckoutSuccessUrl));
        Require(CheckoutCancelUrl, nameof(CheckoutCancelUrl));
        Require(PortalReturnUrl, nameof(PortalReturnUrl));

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Billing:Provider is 'Stripe' but these settings are missing: {string.Join(", ", missing)}. " +
                "Supply them (user-secrets or environment variables) or set Billing:Provider to 'None'.");
        }
    }
}
