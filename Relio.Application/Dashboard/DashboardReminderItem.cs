using Relio.Domain;

namespace Relio.Application.Dashboard;

/// <summary>The minimal reminder details needed by the dashboard.</summary>
/// <param name="Id">The reminder's opaque identifier.</param>
/// <param name="PersonId">The active person's opaque identifier.</param>
/// <param name="PersonDisplayName">The active person's display name.</param>
/// <param name="Title">The user's reminder title.</param>
/// <param name="DueDate">The scheduled date in the user's calendar.</param>
/// <param name="Frequency">How often the reminder repeats.</param>
/// <param name="CustomIntervalMonths">The custom interval, when the reminder uses one.</param>
/// <param name="SnoozedUntilDate">The snooze date, when the reminder is snoozed.</param>
/// <param name="EffectiveDueDate">The date used for dashboard filtering and display.</param>
public sealed record DashboardReminderItem(
    Guid Id,
    Guid PersonId,
    string PersonDisplayName,
    string Title,
    DateOnly DueDate,
    ReminderFrequency Frequency,
    int? CustomIntervalMonths,
    DateOnly? SnoozedUntilDate,
    DateOnly EffectiveDueDate);
