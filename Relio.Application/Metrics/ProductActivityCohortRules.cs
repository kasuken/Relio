using Relio.Domain;

namespace Relio.Application.Metrics;

/// <summary>
/// Pure date rules for the privacy-limited product activity cohort (issue #62).
/// </summary>
/// <remarks>
/// Cohorts are first observed activity dates, not account-registration dates. A cohort is
/// complete for reporting at age 60 and remains reportable through age 89; its stored contribution
/// expires at the start of UTC day 90.
/// </remarks>
public static class ProductActivityCohortRules
{
    /// <summary>The fixed maximum lifetime of a stored cohort contribution.</summary>
    public const int RetentionDays = 90;

    /// <summary>The first age day that qualifies as a return for 30-day retention.</summary>
    public const int ReturnWindowStartDay = 30;

    /// <summary>The last age day that qualifies as a return for 30-day retention.</summary>
    public const int ReturnWindowEndDay = 59;

    /// <summary>The first age day on which a cohort's 30-day return window is complete.</summary>
    public const int CompletedCohortStartDay = 60;

    /// <summary>The final age day on which a completed cohort remains reportable.</summary>
    public const int CompletedCohortEndDay = 89;

    /// <summary>Gets the UTC calendar date represented by <paramref name="timeProvider"/>.</summary>
    /// <param name="timeProvider">The source of the current UTC instant.</param>
    /// <returns>The UTC date, independent of the machine's local time zone.</returns>
    public static DateOnly UtcToday(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
    }

    /// <summary>
    /// Gets the fixed expiry instant for a cohort: midnight UTC at the start of age day 90.
    /// </summary>
    /// <param name="cohortStartedOnUtc">The first observed UTC date.</param>
    /// <returns>The expiration instant, with <see cref="DateTime.Kind"/> set to UTC.</returns>
    public static DateTime RetentionExpiresAtUtc(DateOnly cohortStartedOnUtc) =>
        cohortStartedOnUtc.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(RetentionDays);

    /// <summary>Whether a stored contribution has reached its fixed expiry instant.</summary>
    /// <param name="activity">The stored activity contribution.</param>
    /// <param name="utcNow">The current UTC instant.</param>
    /// <returns><see langword="true"/> at or after expiry.</returns>
    public static bool IsExpired(ProductActivity activity, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return activity.RetentionExpiresAtUtc <= utcNow;
    }

    /// <summary>
    /// Whether an observed activity date falls in the inclusive day-30-to-day-59 return window.
    /// </summary>
    /// <param name="cohortStartedOnUtc">The first observed UTC date.</param>
    /// <param name="activeOnUtc">The UTC date on which activity was observed.</param>
    /// <returns><see langword="true"/> only for cohort ages 30 through 59.</returns>
    public static bool IsWithinReturnWindow(DateOnly cohortStartedOnUtc, DateOnly activeOnUtc)
    {
        var ageInDays = activeOnUtc.DayNumber - cohortStartedOnUtc.DayNumber;
        return ageInDays is >= ReturnWindowStartDay and <= ReturnWindowEndDay;
    }

    /// <summary>
    /// Whether a cohort has completed its return window and is still inside the 90-day lifetime.
    /// </summary>
    /// <param name="cohortStartedOnUtc">The first observed UTC date.</param>
    /// <param name="todayUtc">The current UTC calendar date.</param>
    /// <returns><see langword="true"/> only for cohort ages 60 through 89.</returns>
    public static bool IsCompletedCohort(DateOnly cohortStartedOnUtc, DateOnly todayUtc)
    {
        var ageInDays = todayUtc.DayNumber - cohortStartedOnUtc.DayNumber;
        return ageInDays is >= CompletedCohortStartDay and <= CompletedCohortEndDay;
    }
}
