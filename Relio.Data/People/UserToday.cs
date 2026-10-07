using Microsoft.EntityFrameworkCore;
using Relio.Application.Time;

namespace Relio.Data.People;

/// <summary>
/// Today's date in an owner's own time zone, for the services that validate a calendar date against
/// "today" (a birthday may not be in the future). Shared by <c>PeopleService</c> and
/// <c>PersonMergeService</c>.
/// </summary>
internal static class UserToday
{
    /// <summary>
    /// Today's date in <paramref name="ownerId"/>'s time zone. A missing profile or an unreadable
    /// stored zone falls back to UTC rather than refusing to save a person.
    /// </summary>
    public static async Task<DateOnly> GetAsync(
        RelioDbContext dbContext,
        string ownerId,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var timeZoneId = await dbContext.UserProfiles
            .AsNoTracking()
            .Where(p => p.OwnerId == ownerId)
            .Select(p => p.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);

        var timeZone = TimeZoneIds.TryParse(timeZoneId, out var parsed) ? parsed : TimeZoneInfo.Utc;
        return UserCalendar.Today(timeProvider, timeZone);
    }
}
