using Microsoft.EntityFrameworkCore;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Domain;

namespace Relio.Data.Time;

/// <summary>
/// EF Core-backed implementation of <see cref="IUserTimeZoneService"/>. Lives in Relio.Data (not
/// Relio.Application) because it depends on <see cref="RelioDbContext"/> directly, like
/// <c>Relio.Data.People.PeopleService</c> - see the "User-scoped data pattern" section of
/// AGENTS.md. Every query and mutation is explicitly filtered by <see cref="IOwnedEntity.OwnerId"/>.
/// </summary>
public sealed class UserTimeZoneService(RelioDbContext dbContext, ICurrentUser currentUser, TimeProvider timeProvider)
    : IUserTimeZoneService
{
    /// <inheritdoc />
    public async Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var timeZoneId = await dbContext.UserProfiles
            .AsNoTracking()
            .Where(p => p.OwnerId == ownerId)
            .Select(p => p.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);

        // No profile yet (e.g. sign-up, #15, has not run) - UTC, same as a brand new UserProfile.
        return TimeZoneIds.Parse(timeZoneId ?? TimeZoneIds.Default);
    }

    /// <inheritdoc />
    public async Task<DateOnly> GetTodayAsync(CancellationToken cancellationToken = default)
    {
        var timeZone = await GetTimeZoneAsync(cancellationToken);
        return UserCalendar.Today(timeProvider, timeZone);
    }

    /// <inheritdoc />
    public async Task SetTimeZoneAsync(string ianaTimeZoneId, CancellationToken cancellationToken = default)
    {
        // Validate before touching the database: a bad id must leave the existing profile
        // (if any) untouched.
        var timeZone = TimeZoneIds.Parse(ianaTimeZoneId);
        var ownerId = currentUser.RequireUserId();

        var profile = await dbContext.UserProfiles
            .FirstOrDefaultAsync(p => p.OwnerId == ownerId, cancellationToken);

        if (profile is null)
        {
            dbContext.UserProfiles.Add(new UserProfile { OwnerId = ownerId, TimeZoneId = timeZone.Id });
        }
        else
        {
            profile.TimeZoneId = timeZone.Id;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsDueTodayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var today = await GetTodayAsync(cancellationToken);
        return UserCalendar.IsDueToday(date, today);
    }

    /// <inheritdoc />
    public async Task<bool> IsOverdueAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var today = await GetTodayAsync(cancellationToken);
        return UserCalendar.IsOverdue(date, today);
    }
}
