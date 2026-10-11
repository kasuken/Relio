namespace Relio.Application.Billing;

/// <summary>Supported billing provider implementations.</summary>
public enum BillingProviderType
{
    /// <summary>
    /// Safe default: no billing provider. There are no plans or limits, no payment UI is shown and
    /// webhooks are refused. Every self-hosted instance runs like this.
    /// </summary>
    None,

    /// <summary>
    /// Stripe: hosted Checkout to subscribe, the hosted customer portal for self-service changes,
    /// and signature-verified webhooks that drive the plan. See <c>docs/security/billing.md</c>.
    /// </summary>
    Stripe,
}
