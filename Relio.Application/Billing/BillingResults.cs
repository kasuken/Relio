namespace Relio.Application.Billing;

/// <summary>
/// Result of creating a checkout session.
/// </summary>
public sealed record CheckoutSessionResult(bool Success, string? Url, string? ErrorMessage = null);

/// <summary>
/// Result of creating a customer portal session.
/// </summary>
public sealed record PortalSessionResult(bool Success, string? Url, string? ErrorMessage = null);

/// <summary>
/// Result of verifying an inbound webhook signature.
/// </summary>
public sealed record WebhookVerificationResult(bool Success, string? ErrorMessage = null);
