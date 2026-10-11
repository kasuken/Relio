using Relio.Application.Billing;
using Relio.Application.Time;
using Relio.Web.Time;

namespace Relio.Web.Components.Billing;

/// <summary>All the words of the plan page and the plan-limit notices. Pure, so it is unit tested.</summary>
public static class PlanText
{
    /// <summary>The plan page, a static SSR page because it redirects to Stripe.</summary>
    public const string PlanPagePath = "/Account/Manage/Plan";

    public const string CheckoutCancelled = "Checkout was cancelled. You have not been charged.";

    public const string CheckoutConfirmed = "Welcome to Relio Pro. Your subscription is active.";

    public const string CheckoutPending =
        "Payment received. Your subscription is being activated; refresh this page in a moment.";

    public const string CheckoutFailed = "Relio couldn't start checkout. Please try again.";

    public const string PortalFailed = "Relio couldn't open the billing portal. Please try again.";

    public const string PaidAccessSuspended =
        "Your Relio Pro access is paused because a payment didn't go through. Update your payment " +
        "method under Manage billing to restore it.";

    public const string StripeNotice =
        "Payments are handled by Stripe. Relio sends Stripe your account email and an opaque account " +
        "id only - never anything you have written about people.";

    /// <summary>"12 of 25 active people" or "40 active people (unlimited)".</summary>
    public static string Usage(PlanSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var noun = summary.ActivePeople == 1 ? "active person" : "active people";
        return summary.MaxActivePeople is int max
            ? $"{summary.ActivePeople} of {max} active people"
            : $"{summary.ActivePeople} {noun} (unlimited)";
    }

    /// <summary>The notice shown when the free plan's limit is reached.</summary>
    public static string LimitReached(int limit) =>
        $"You've reached the Free plan's limit of {limit} active people. Archive someone, or " +
        "subscribe to Relio Pro for unlimited people.";

    /// <summary>The notice shown when a save, restore, import or merge was refused by the limit.</summary>
    public static string LimitRefused(int limit) =>
        $"The Free plan allows up to {limit} active people, so nothing was saved. Archive someone, " +
        "or subscribe to Relio Pro for unlimited people.";

    /// <summary>The grace-period notice after a failed payment.</summary>
    public static string GracePeriod(DateTime gracePeriodEndsAtUtc, TimeZoneInfo timeZone, DateOnly today) =>
        $"A recent payment didn't go through. Relio Pro continues until {Date(gracePeriodEndsAtUtc, timeZone, today)}; " +
        "update your payment method under Manage billing to keep it.";

    /// <summary>"Your subscription ends on 3 March. ..." for a cancelled plan.</summary>
    public static string Cancels(DateTime cancelsAtUtc, TimeZoneInfo timeZone, DateOnly today) =>
        $"Your subscription is cancelled and ends on {Date(cancelsAtUtc, timeZone, today)}. You can resume it under Manage billing before then.";

    /// <summary>"Renews on 3 March 2027."</summary>
    public static string Renews(DateTime renewsAtUtc, TimeZoneInfo timeZone, DateOnly today) =>
        $"Renews on {Date(renewsAtUtc, timeZone, today)}.";

    /// <summary>"Subscribe yearly - $12 / year ($1 / month)".</summary>
    public static string SubscribeYearly =>
        $"Subscribe yearly - {PlanCatalog.YearlyPriceText} ({PlanCatalog.YearlyMonthlyEquivalentText})";

    /// <summary>"Subscribe monthly - $2 / month".</summary>
    public static string SubscribeMonthly => $"Subscribe monthly - {PlanCatalog.MonthlyPriceText}";

    /// <summary>
    /// A billing instant as a calendar day in the user's time zone. SQL Server returns it with kind
    /// Unspecified, so it is treated as UTC.
    /// </summary>
    public static string Date(DateTime utc, TimeZoneInfo timeZone, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var instant = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        return DateDisplay.Format(UserCalendar.ToUserDate(instant, timeZone), today);
    }
}
