namespace Relio.Application.Billing;

/// <summary>
/// Represents a subscription plan definition.
/// </summary>
public sealed record BillingPlan(
    string Name,
    string DisplayName,
    bool IsUnlimited,
    string? MonthlyPriceId = null,
    string? YearlyPriceId = null);
