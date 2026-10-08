namespace Relio.Application.Billing;

/// <summary>
/// Supported billing provider implementations.
/// </summary>
public enum BillingProviderType
{
    /// <summary>
    /// Safe default: no billing provider configured. Hosted billing is disabled,
    /// all features run unconstrained, and no payment UI is displayed.
    /// </summary>
    None,

    /// <summary>
    /// Stripe hosted billing integration for paid plans.
    /// </summary>
    Stripe,
}
