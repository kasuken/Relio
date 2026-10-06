using Microsoft.EntityFrameworkCore;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Data.Profile;
using Relio.Data.Tests.People;
using Relio.Data.Time;
using Relio.Domain;

namespace Relio.Data.Tests.Profile;

/// <summary>
/// Proves issue #18's display name requirements through <see cref="UserProfileService"/>: it
/// round-trips, is trimmed and bounded, never disturbs the time zone, and - like every other owned
/// entity (see the "User-scoped data pattern" section of AGENTS.md) - is isolated per user.
/// </summary>
public class UserProfileServiceTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    [Fact]
    public async Task GetDisplayNameAsync_returns_null_when_no_profile_exists()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA);

        (await service.GetDisplayNameAsync()).Should().BeNull();
    }

    [Fact]
    public async Task SetDisplayNameAsync_then_GetDisplayNameAsync_round_trips()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA);

        await service.SetDisplayNameAsync("Emanuele");

        (await service.GetDisplayNameAsync()).Should().Be("Emanuele");
    }

    [Fact]
    public async Task SetDisplayNameAsync_trims_whitespace()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA);

        await service.SetDisplayNameAsync("  Emanuele  ");

        (await service.GetDisplayNameAsync()).Should().Be("Emanuele");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetDisplayNameAsync_with_null_or_whitespace_clears_it(string? value)
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA);
        await service.SetDisplayNameAsync("Emanuele");

        await service.SetDisplayNameAsync(value);

        (await service.GetDisplayNameAsync()).Should().BeNull();
    }

    [Fact]
    public async Task SetDisplayNameAsync_accepts_exactly_the_maximum_length()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA);
        var name = new string('a', UserProfile.DisplayNameMaxLength);

        await service.SetDisplayNameAsync(name);

        (await service.GetDisplayNameAsync()).Should().Be(name);
    }

    [Fact]
    public async Task SetDisplayNameAsync_longer_than_the_max_throws_and_leaves_the_profile_unchanged()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA);
        await service.SetDisplayNameAsync("Emanuele");

        var act = () => service.SetDisplayNameAsync(new string('a', UserProfile.DisplayNameMaxLength + 1));

        await act.Should().ThrowAsync<ArgumentException>();
        (await service.GetDisplayNameAsync()).Should().Be("Emanuele");
    }

    [Fact]
    public async Task SetDisplayNameAsync_creates_a_profile_with_the_default_time_zone_when_missing()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA);

        await service.SetDisplayNameAsync("Emanuele");

        var profile = await dbContext.UserProfiles.AsNoTracking().SingleAsync(p => p.OwnerId == UserA);
        profile.TimeZoneId.Should().Be(TimeZoneIds.Default);
        profile.DisplayName.Should().Be("Emanuele");
    }

    [Fact]
    public async Task SetDisplayNameAsync_does_not_change_the_time_zone()
    {
        await using var dbContext = CreateDbContext();
        await CreateTimeZoneService(dbContext, UserA).SetTimeZoneAsync("Pacific/Kiritimati");
        var service = CreateService(dbContext, UserA);

        await service.SetDisplayNameAsync("Emanuele");

        var profile = await dbContext.UserProfiles.AsNoTracking().SingleAsync(p => p.OwnerId == UserA);
        profile.TimeZoneId.Should().Be("Pacific/Kiritimati");
    }

    [Fact]
    public async Task Setting_the_time_zone_does_not_change_the_display_name()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA);
        await service.SetDisplayNameAsync("Emanuele");

        await CreateTimeZoneService(dbContext, UserA).SetTimeZoneAsync("Europe/Rome");

        (await service.GetDisplayNameAsync()).Should().Be("Emanuele");
    }

    [Fact]
    public async Task SetDisplayNameAsync_twice_updates_the_same_profile_rather_than_creating_a_second_one()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA);

        await service.SetDisplayNameAsync("Emanuele");
        await service.SetDisplayNameAsync("Manu");

        (await dbContext.UserProfiles.CountAsync()).Should().Be(1);
        (await service.GetDisplayNameAsync()).Should().Be("Manu");
    }

    [Fact]
    public async Task Operations_without_an_authenticated_user_throw()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, userId: null);

        var get = () => service.GetDisplayNameAsync();
        var set = () => service.SetDisplayNameAsync("Emanuele");

        await get.Should().ThrowAsync<UnauthenticatedUserException>();
        await set.Should().ThrowAsync<UnauthenticatedUserException>();
    }

    [Fact]
    public async Task User_A_cannot_read_user_Bs_display_name()
    {
        await using var dbContext = CreateDbContext();
        await CreateService(dbContext, UserB).SetDisplayNameAsync("Bea");

        (await CreateService(dbContext, UserA).GetDisplayNameAsync()).Should().BeNull();
    }

    [Fact]
    public async Task User_A_cannot_overwrite_user_Bs_display_name()
    {
        await using var dbContext = CreateDbContext();
        await CreateService(dbContext, UserB).SetDisplayNameAsync("Bea");

        await CreateService(dbContext, UserA).SetDisplayNameAsync("Alice");

        (await CreateService(dbContext, UserB).GetDisplayNameAsync()).Should().Be("Bea");
        (await CreateService(dbContext, UserA).GetDisplayNameAsync()).Should().Be("Alice");
    }

    [Fact]
    public async Task SetDisplayNameAsync_leaves_nothing_tracked()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA);

        await service.SetDisplayNameAsync("Ada");
        dbContext.ChangeTracker.Entries().Should().BeEmpty("creating the profile clears the tracker");

        await service.SetDisplayNameAsync("Bea");
        dbContext.ChangeTracker.Entries().Should().BeEmpty("updating the profile clears the tracker");
    }

    [Fact]
    public async Task A_failed_save_does_not_make_the_next_save_insert_the_profile_again()
    {
        var failing = new FailOnceSaveChangesInterceptor();
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(failing)
            .Options;
        await using var dbContext = new RelioDbContext(options, TimeProvider.System);
        var service = CreateService(dbContext, UserA);

        var act = () => service.SetDisplayNameAsync("Ada");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*simulated*");
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await service.SetDisplayNameAsync("Bea");

        (await dbContext.UserProfiles.AsNoTracking().Where(p => p.OwnerId == UserA).ToListAsync())
            .Should().ContainSingle().Which.DisplayName.Should().Be("Bea");
    }

    private static RelioDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RelioDbContext(options, TimeProvider.System);
    }

    private static UserProfileService CreateService(RelioDbContext dbContext, string? userId) =>
        new(dbContext, new FakeCurrentUser(userId));

    private static UserTimeZoneService CreateTimeZoneService(RelioDbContext dbContext, string userId) =>
        new(dbContext, new FakeCurrentUser(userId), TimeProvider.System);
}
