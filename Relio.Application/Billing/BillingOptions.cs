namespace Relio.Application.Billing;

/// <summary>
/// Configuration options for hosted billing (<c>Billing</c> section). Off by default: with
/// <see cref="BillingProviderType.None"/> there are no plans, no limits and no payment UI, which is
/// how every self-hosted instance runs.
/// </summary>
/// <remarks>
/// Secrets (<see cref="ApiKey"/>, <see cref="WebhookSigningSecret"/>) never go in
/// <c>appsettings*.json</c>: use user secrets locally and environment variables or a secret store
/// (Key Vault references) when hosting.
/// </remarks>
public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>The billing provider to use. Defaults to <see cref="BillingProviderType.None"/>.</summary>
    public BillingProviderType Provider { get; set; } = BillingProviderType.None;

    /// <summary>Stripe secret API key (<c>sk_test_...</c> or <c>sk_live_...</c>).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Signing secret of the Stripe webhook endpoint (<c>whsec_...</c>).</summary>
    public string? WebhookSigningSecret { get; set; }

    /// <summary>
    /// Stripe price id of Relio Pro billed monthly. It is both the price a monthly checkout charges
    /// and one of the ids a subscription is matched against to decide it is a Relio subscription.
    /// </summary>
    public string? ProMonthlyPriceId { get; set; }

    /// <summary>Stripe price id of Relio Pro billed yearly, for the same two purposes as <see cref="ProMonthlyPriceId"/>.</summary>
    public string? ProYearlyPriceId { get; set; }

    /// <summary>
    /// Absolute URL Stripe returns to after a completed checkout (normally
    /// <c>https://&lt;host&gt;/Account/Manage/Plan</c>). Kept in configuration rather than derived from
    /// the request, so a proxy's host header can never redirect a payer elsewhere.
    /// </summary>
    public string? CheckoutSuccessUrl { get; set; }

    /// <summary>Absolute URL Stripe returns to when checkout is abandoned.</summary>
    public string? CheckoutCancelUrl { get; set; }

    /// <summary>Absolute URL the Stripe customer portal returns to.</summary>
    public string? PortalReturnUrl { get; set; }

    /// <summary>True when a live payment provider is configured, so plans and limits apply.</summary>
    public bool IsEnabled => Provider != BillingProviderType.None;

    /// <summary>
    /// Validates the Stripe configuration, throwing <see cref="InvalidOperationException"/> naming
    /// every missing setting when <see cref="Provider"/> is <see cref="BillingProviderType.Stripe"/>.
    /// The message names settings only, never their values.
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
                "Supply them (user secrets or environment variables) or set Billing:Provider to 'None'.");
        }

        RequireAbsoluteHttps(CheckoutSuccessUrl!, nameof(CheckoutSuccessUrl));
        RequireAbsoluteHttps(CheckoutCancelUrl!, nameof(CheckoutCancelUrl));
        RequireAbsoluteHttps(PortalReturnUrl!, nameof(PortalReturnUrl));
    }

    private static void RequireAbsoluteHttps(string value, string name)
    {
        // Stripe accepts plain http only in test mode; localhost is allowed so `stripe listen`
        // works during development.
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{name} must be an absolute https URL (http is allowed for localhost only).");
        }
    }
}
