namespace Relio.Application.Metrics;

/// <summary>
/// Provides only instance-wide aggregate product metrics to an active Administrator.
/// </summary>
/// <remarks>
/// Implementations must re-check the caller's active Administrator role in the database for each
/// request. Reports contain counts and rates only: never names, notes, descriptions, per-user rows,
/// or arbitrary narrow filters.
/// </remarks>
public interface IProductMetricsReportService
{
    /// <summary>Gets the current aggregate report, or a static disabled status when metrics are off.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>An administrator-authorized aggregate report with no user-level breakdown.</returns>
    Task<ProductMetricsReport> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Aggregate-only product metrics. A disabled report has <see langword="null"/> metric values;
/// a <see langword="null"/> rate means no denominator or no completed cohort history, not zero.
/// </summary>
/// <param name="IsEnabled">Whether an operator has enabled product metrics.</param>
/// <param name="LiveAccountCount">Every existing Identity account row, including disabled and unconfirmed accounts.</param>
/// <param name="OwnedPeopleCount">All owned people rows, including archived people.</param>
/// <param name="AveragePeoplePerAccount">People divided by all existing Identity account rows, or null when there are no accounts.</param>
/// <param name="CurrentUtcMonthStart">The first UTC calendar date of the interaction reporting month.</param>
/// <param name="InteractionsThisUtcMonth">Interactions whose stored OccurredOn is in the current UTC calendar month.</param>
/// <param name="SavedReminderCount">All saved reminders, including completed reminders and reminders for archived people.</param>
/// <param name="AccountsWithSavedReminders">Existing accounts with at least one saved reminder.</param>
/// <param name="AccountsWithSavedRemindersPercent">Accounts with a reminder divided by all existing accounts, or null when there are no accounts.</param>
/// <param name="EligibleRetentionCohorts">Observed cohorts aged 60 through 89 UTC days.</param>
/// <param name="ReturnedRetentionCohorts">Eligible cohorts with activity observed on cohort age day 30 through 59.</param>
/// <param name="RetentionRatePercent">Returned cohorts divided by eligible cohorts, or null when no completed cohorts exist.</param>
public sealed record ProductMetricsReport(
    bool IsEnabled,
    long? LiveAccountCount,
    long? OwnedPeopleCount,
    decimal? AveragePeoplePerAccount,
    DateOnly? CurrentUtcMonthStart,
    long? InteractionsThisUtcMonth,
    long? SavedReminderCount,
    long? AccountsWithSavedReminders,
    decimal? AccountsWithSavedRemindersPercent,
    long? EligibleRetentionCohorts,
    long? ReturnedRetentionCohorts,
    decimal? RetentionRatePercent)
{
    /// <summary>A static off status with no collected or queried metrics.</summary>
    public static ProductMetricsReport Disabled { get; } = new(
        IsEnabled: false,
        LiveAccountCount: null,
        OwnedPeopleCount: null,
        AveragePeoplePerAccount: null,
        CurrentUtcMonthStart: null,
        InteractionsThisUtcMonth: null,
        SavedReminderCount: null,
        AccountsWithSavedReminders: null,
        AccountsWithSavedRemindersPercent: null,
        EligibleRetentionCohorts: null,
        ReturnedRetentionCohorts: null,
        RetentionRatePercent: null);
}
