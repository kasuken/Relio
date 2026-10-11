namespace Relio.Application.Billing;

/// <summary>A hosted plan tier. Only meaningful while billing is enabled.</summary>
public enum PlanTier
{
    /// <summary>The free plan: up to <see cref="PlanCatalog.FreeActivePeopleLimit"/> active people.</summary>
    Free,

    /// <summary>Relio Pro: unlimited people.</summary>
    Pro,
}

/// <summary>How often Relio Pro is charged.</summary>
public enum BillingInterval
{
    /// <summary>Charged every month.</summary>
    Monthly,

    /// <summary>Charged once a year.</summary>
    Yearly,
}

/// <summary>
/// One row of the plan matrix: every marketed capability mapped to a value the services enforce.
/// </summary>
/// <param name="Tier">The tier this row describes.</param>
/// <param name="DisplayName">User-facing plan name.</param>
/// <param name="MaxActivePeople">
/// Most people that may be active (not archived) at once; null means unlimited. Enforced by
/// <c>Relio.Data.Billing.PlanLimits</c> whenever a person is added or restored.
/// </param>
/// <param name="Capabilities">User-facing bullet list for the pricing and plan pages.</param>
public sealed record PlanDefinition(
    PlanTier Tier,
    string DisplayName,
    int? MaxActivePeople,
    IReadOnlyList<string> Capabilities);

/// <summary>
/// The hosted plan matrix and its prices. The prices here are display copy: the amount actually
/// charged is the Stripe price configured in <see cref="BillingOptions.ProMonthlyPriceId"/> and
/// <see cref="BillingOptions.ProYearlyPriceId"/>, which must be kept in step with these values.
/// </summary>
public static class PlanCatalog
{
    /// <summary>Active people allowed on the free plan. Archived people do not count.</summary>
    public const int FreeActivePeopleLimit = 25;

    /// <summary>Relio Pro billed monthly, in US dollars.</summary>
    public const decimal ProMonthlyPriceUsd = 2m;

    /// <summary>Relio Pro billed yearly, in US dollars (the equivalent of $1 a month).</summary>
    public const decimal ProYearlyPriceUsd = 12m;

    public static readonly PlanDefinition Free = new(
        Tier: PlanTier.Free,
        DisplayName: "Free",
        MaxActivePeople: FreeActivePeopleLimit,
        Capabilities:
        [
            $"Up to {FreeActivePeopleLimit} active people",
            "Interactions, notes, reminders and difficult moments",
            "Export and account deletion at any time",
        ]);

    public static readonly PlanDefinition Pro = new(
        Tier: PlanTier.Pro,
        DisplayName: "Relio Pro",
        MaxActivePeople: null,
        Capabilities:
        [
            "Everything in Free",
            "Unlimited people",
            "Supports the running of this hosted instance",
        ]);

    /// <summary>All plans, in display order.</summary>
    public static readonly IReadOnlyList<PlanDefinition> All = [Free, Pro];

    /// <summary>"$2 / month".</summary>
    public static string MonthlyPriceText => $"${ProMonthlyPriceUsd:0} / month";

    /// <summary>"$12 / year".</summary>
    public static string YearlyPriceText => $"${ProYearlyPriceUsd:0} / year";

    /// <summary>"$1 / month", the yearly price spread over twelve months.</summary>
    public static string YearlyMonthlyEquivalentText => $"${ProYearlyPriceUsd / 12m:0.##} / month";

    public static PlanDefinition Get(PlanTier tier) => tier switch
    {
        PlanTier.Free => Free,
        PlanTier.Pro => Pro,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown plan tier."),
    };
}
