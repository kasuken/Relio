using Microsoft.EntityFrameworkCore;
using Relio.Application.Reminders;
using Relio.Application.Security;
using Relio.Data.Reminders;
using Relio.Data.Tests.People;
using Relio.Domain;

namespace Relio.Data.Tests.Reminders;

public class NotificationPreferencesServiceTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    [Fact]
    public async Task GetPreferencesAsync_returns_defaults_and_generates_token_when_no_profile_exists()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA);

        var prefs = await service.GetPreferencesAsync();

        prefs.Delivery.Should().Be(ReminderEmailDelivery.DailyDigest);
        prefs.BirthdayRemindersEnabled.Should().BeTrue();
        prefs.DefaultBirthdayLeadDays.Should().Be(0);
        prefs.UnsubscribeToken.Should().NotBeNullOrWhiteSpace();

        // Stored profile exists with that token
        var profile = await dbContext.UserProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.OwnerId == UserA);
        profile.Should().NotBeNull();
        profile!.UnsubscribeToken.Should().Be(prefs.UnsubscribeToken);
    }

    [Fact]
    public async Task GetPreferencesAsync_reuses_existing_token_when_already_present()
    {
        await using var dbContext = CreateDbContext();
        dbContext.UserProfiles.Add(new UserProfile
        {
            OwnerId = UserA,
            TimeZoneId = "UTC",
            ReminderEmailDelivery = ReminderEmailDelivery.Immediate,
            BirthdayRemindersEnabled = false,
            DefaultBirthdayLeadDays = 3,
            UnsubscribeToken = "existing-token-123",
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = CreateService(dbContext, UserA);
        var prefs = await service.GetPreferencesAsync();

        prefs.Delivery.Should().Be(ReminderEmailDelivery.Immediate);
        prefs.BirthdayRemindersEnabled.Should().BeFalse();
        prefs.DefaultBirthdayLeadDays.Should().Be(3);
        prefs.UnsubscribeToken.Should().Be("existing-token-123");
    }

    [Fact]
    public async Task GetPreferencesAsync_generates_token_when_profile_exists_without_token()
    {
        await using var dbContext = CreateDbContext();
        dbContext.UserProfiles.Add(new UserProfile
        {
            OwnerId = UserA,
            TimeZoneId = "Europe/Rome",
            DisplayName = "Test User",
            UnsubscribeToken = null,
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = CreateService(dbContext, UserA);
        var prefs = await service.GetPreferencesAsync();

        prefs.UnsubscribeToken.Should().NotBeNullOrWhiteSpace();

        var profile = await dbContext.UserProfiles.AsNoTracking().SingleAsync(p => p.OwnerId == UserA);
        profile.UnsubscribeToken.Should().Be(prefs.UnsubscribeToken);
        profile.TimeZoneId.Should().Be("Europe/Rome");
        profile.DisplayName.Should().Be("Test User");
    }

    [Fact]
    public async Task SetPreferencesAsync_then_GetPreferencesAsync_round_trips()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA);

        await service.SetPreferencesAsync(new UpdateNotificationPreferencesRequest(
            ReminderEmailDelivery.None,
            BirthdayRemindersEnabled: false,
            DefaultBirthdayLeadDays: 7));

        var prefs = await service.GetPreferencesAsync();
        prefs.Delivery.Should().Be(ReminderEmailDelivery.None);
        prefs.BirthdayRemindersEnabled.Should().BeFalse();
        prefs.DefaultBirthdayLeadDays.Should().Be(7);
        prefs.UnsubscribeToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SetPreferencesAsync_preserves_existing_display_name_and_time_zone()
    {
        await using var dbContext = CreateDbContext();
        dbContext.UserProfiles.Add(new UserProfile
        {
            OwnerId = UserA,
            TimeZoneId = "America/New_York",
            DisplayName = "Alice",
            UnsubscribeToken = "token-xyz",
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = CreateService(dbContext, UserA);
        await service.SetPreferencesAsync(new UpdateNotificationPreferencesRequest(
            ReminderEmailDelivery.Immediate,
            BirthdayRemindersEnabled: true,
            DefaultBirthdayLeadDays: 1));

        var profile = await dbContext.UserProfiles.AsNoTracking().SingleAsync(p => p.OwnerId == UserA);
        profile.TimeZoneId.Should().Be("America/New_York");
        profile.DisplayName.Should().Be("Alice");
        profile.UnsubscribeToken.Should().Be("token-xyz");
        profile.ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.Immediate);
        profile.DefaultBirthdayLeadDays.Should().Be(1);
    }

    [Fact]
    public async Task SetPreferencesAsync_rejects_invalid_delivery()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA);

        var act = () => service.SetPreferencesAsync(new UpdateNotificationPreferencesRequest(
            (ReminderEmailDelivery)99,
            BirthdayRemindersEnabled: true,
            DefaultBirthdayLeadDays: 0));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task SetPreferencesAsync_rejects_negative_lead_days()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA);

        var act = () => service.SetPreferencesAsync(new UpdateNotificationPreferencesRequest(
            ReminderEmailDelivery.Immediate,
            BirthdayRemindersEnabled: true,
            DefaultBirthdayLeadDays: -1));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Operations_without_an_authenticated_user_throw()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, userId: null);

        var get = () => service.GetPreferencesAsync();
        var set = () => service.SetPreferencesAsync(new UpdateNotificationPreferencesRequest(
            ReminderEmailDelivery.DailyDigest,
            BirthdayRemindersEnabled: true,
            DefaultBirthdayLeadDays: 0));

        await get.Should().ThrowAsync<UnauthenticatedUserException>();
        await set.Should().ThrowAsync<UnauthenticatedUserException>();
    }

    [Fact]
    public async Task User_isolation_is_enforced()
    {
        await using var dbContext = CreateDbContext();
        var serviceA = CreateService(dbContext, UserA);
        var serviceB = CreateService(dbContext, UserB);

        await serviceA.SetPreferencesAsync(new UpdateNotificationPreferencesRequest(
            ReminderEmailDelivery.None,
            BirthdayRemindersEnabled: false,
            DefaultBirthdayLeadDays: 14));

        var prefsB = await serviceB.GetPreferencesAsync();
        prefsB.Delivery.Should().Be(ReminderEmailDelivery.DailyDigest);
        prefsB.BirthdayRemindersEnabled.Should().BeTrue();
        prefsB.DefaultBirthdayLeadDays.Should().Be(0);

        var prefsA = await serviceA.GetPreferencesAsync();
        prefsA.Delivery.Should().Be(ReminderEmailDelivery.None);
        prefsA.BirthdayRemindersEnabled.Should().BeFalse();
        prefsA.DefaultBirthdayLeadDays.Should().Be(14);
    }

    [Fact]
    public async Task SetPreferencesAsync_leaves_nothing_tracked()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA);

        await service.SetPreferencesAsync(new UpdateNotificationPreferencesRequest(
            ReminderEmailDelivery.DailyDigest,
            BirthdayRemindersEnabled: true,
            DefaultBirthdayLeadDays: 3));

        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    private static RelioDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RelioDbContext(options, TimeProvider.System, FieldProtector);
    }

    private static NotificationPreferencesService CreateService(RelioDbContext dbContext, string? userId) =>
        new(dbContext, new FakeCurrentUser(userId));
}
