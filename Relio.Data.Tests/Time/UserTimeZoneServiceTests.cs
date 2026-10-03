using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Data.Tests.People;
using Relio.Data.Time;

namespace Relio.Data.Tests.Time;

/// <summary>
/// Proves issue #12's acceptance criteria end to end through
/// <see cref="Relio.Data.Time.UserTimeZoneService"/>: a user's time zone defaults to UTC until
/// set, round-trips through the database, is validated against known IANA ids, and - like every
/// other owned entity (see the "User-scoped data pattern" section of AGENTS.md) - is isolated per
/// user.
/// </summary>
public class UserTimeZoneServiceTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    [Fact]
    public async Task GetTimeZoneAsync_defaults_to_UTC_when_no_profile_exists()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA, new FakeTimeProvider());

        var timeZone = await service.GetTimeZoneAsync();

        timeZone.Id.Should().Be("UTC");
    }

    [Fact]
    public async Task SetTimeZoneAsync_then_GetTimeZoneAsync_round_trips_through_the_database()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA, new FakeTimeProvider());

        await service.SetTimeZoneAsync("Europe/Rome");
        var timeZone = await service.GetTimeZoneAsync();

        timeZone.Id.Should().Be("Europe/Rome");
    }

    [Fact]
    public async Task SetTimeZoneAsync_called_twice_updates_the_same_profile_rather_than_creating_a_second_one()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA, new FakeTimeProvider());

        await service.SetTimeZoneAsync("Europe/Rome");
        await service.SetTimeZoneAsync("Pacific/Kiritimati");

        (await dbContext.UserProfiles.CountAsync()).Should().Be(1);
        (await service.GetTimeZoneAsync()).Id.Should().Be("Pacific/Kiritimati");
    }

    [Fact]
    public async Task SetTimeZoneAsync_with_an_unknown_id_throws_and_does_not_change_the_profile()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA, new FakeTimeProvider());
        await service.SetTimeZoneAsync("Europe/Rome");

        var act = () => service.SetTimeZoneAsync("Not/AZone");

        await act.Should().ThrowAsync<InvalidTimeZoneIdException>();
        (await service.GetTimeZoneAsync()).Id.Should().Be("Europe/Rome");
    }

    [Fact]
    public async Task GetTodayAsync_returns_the_calendar_date_in_the_users_time_zone()
    {
        await using var dbContext = CreateDbContext();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2024-03-01T20:00:00Z"));
        var service = CreateService(dbContext, UserA, timeProvider);
        await service.SetTimeZoneAsync("Pacific/Kiritimati"); // UTC+14: already the next day.

        (await service.GetTodayAsync()).Should().Be(new DateOnly(2024, 3, 2));
    }

    [Fact]
    public async Task IsDueTodayAsync_and_IsOverdueAsync_use_the_users_time_zone()
    {
        await using var dbContext = CreateDbContext();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2024-03-02T03:00:00Z"));
        var service = CreateService(dbContext, UserA, timeProvider);
        await service.SetTimeZoneAsync("Pacific/Pago_Pago"); // UTC-11: still the previous day.

        (await service.IsDueTodayAsync(new DateOnly(2024, 3, 1))).Should().BeTrue();
        (await service.IsOverdueAsync(new DateOnly(2024, 3, 1))).Should().BeFalse();
        (await service.IsOverdueAsync(new DateOnly(2024, 2, 29))).Should().BeTrue();
    }

    [Fact]
    public async Task Operations_without_an_authenticated_user_throw()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, userId: null, new FakeTimeProvider());

        var act = () => service.GetTimeZoneAsync();

        await act.Should().ThrowAsync<UnauthenticatedUserException>();
    }

    [Fact]
    public async Task User_A_cannot_read_user_Bs_time_zone()
    {
        await using var dbContext = CreateDbContext();
        await CreateService(dbContext, UserB, new FakeTimeProvider()).SetTimeZoneAsync("Pacific/Kiritimati");

        var serviceForA = CreateService(dbContext, UserA, new FakeTimeProvider());
        var timeZoneForA = await serviceForA.GetTimeZoneAsync();

        // User A has no profile of their own, so they see the default - never user B's time zone.
        timeZoneForA.Id.Should().Be("UTC");
        (await dbContext.UserProfiles.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task User_A_cannot_overwrite_user_Bs_time_zone()
    {
        await using var dbContext = CreateDbContext();
        await CreateService(dbContext, UserB, new FakeTimeProvider()).SetTimeZoneAsync("Pacific/Kiritimati");

        await CreateService(dbContext, UserA, new FakeTimeProvider()).SetTimeZoneAsync("Pacific/Pago_Pago");

        var profileForB = await dbContext.UserProfiles.AsNoTracking().SingleAsync(p => p.OwnerId == UserB);
        profileForB.TimeZoneId.Should().Be("Pacific/Kiritimati");

        var profileForA = await dbContext.UserProfiles.AsNoTracking().SingleAsync(p => p.OwnerId == UserA);
        profileForA.TimeZoneId.Should().Be("Pacific/Pago_Pago");
    }

    private static RelioDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RelioDbContext(options, TimeProvider.System);
    }

    private static UserTimeZoneService CreateService(RelioDbContext dbContext, string? userId, TimeProvider timeProvider) =>
        new(dbContext, new FakeCurrentUser(userId), timeProvider);
}
