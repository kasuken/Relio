using Relio.Application.Reminders;

namespace Relio.Application.Dashboard;

/// <summary>A point-in-time, user-scoped overview for the dashboard.</summary>
/// <param name="Today">Today's date in the current user's calendar.</param>
/// <param name="ActivePeopleCount">The number of active people owned by the current user.</param>
/// <param name="ArchivedPeopleCount">The number of archived people owned by the current user.</param>
/// <param name="UpcomingReminders">The five earliest incomplete reminders due within the next 30 days, including overdue reminders.</param>
/// <param name="UpcomingBirthdays">The next birthdays within 30 days, respecting account and person settings.</param>
/// <param name="ReachOuts">The most overdue active people with a stay-in-touch cadence.</param>
/// <param name="RecentInteractions">The latest interactions with at least one active participant owned by the current user.</param>
/// <param name="RecentlyAddedPeople">The five newest active people owned by the current user.</param>
public sealed record DashboardSnapshot(
    DateOnly Today,
    int ActivePeopleCount,
    int ArchivedPeopleCount,
    IReadOnlyList<DashboardReminderItem> UpcomingReminders,
    IReadOnlyList<BirthdayReminderDto> UpcomingBirthdays,
    IReadOnlyList<ReachOutDto> ReachOuts,
    IReadOnlyList<DashboardInteractionItem> RecentInteractions,
    IReadOnlyList<DashboardPersonItem> RecentlyAddedPeople);
