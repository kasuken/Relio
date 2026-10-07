using Microsoft.EntityFrameworkCore;
using Relio.Data.Encryption;
using Relio.Data.Reminders;
using Relio.Domain;

namespace Relio.Data.Tests.Reminders;

public class UnsubscribeServiceTests
{
    [Fact]
    public async Task UnsubscribeAsync_with_valid_token_sets_delivery_to_none_and_returns_true()
    {
        await using var dbContext = CreateDbContext();
        dbContext.UserProfiles.Add(new UserProfile
        {
            OwnerId = "user-1",
            TimeZoneId = "UTC",
            ReminderEmailDelivery = ReminderEmailDelivery.DailyDigest,
            UnsubscribeToken = "token-123",
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = new UnsubscribeService(dbContext);
        var result = await service.UnsubscribeAsync("token-123");

        result.Should().BeTrue();

        var profile = await dbContext.UserProfiles.AsNoTracking().SingleAsync(p => p.OwnerId == "user-1");
        profile.ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.None);
    }

    [Fact]
    public async Task Encrypted_unsubscribe_token_keeps_a_stable_verifier_for_repeatable_lookup()
    {
        const string token = "synthetic-high-entropy-unsubscribe-token";
        var options = CreateOptions();
        await using var dbContext = new RelioDbContext(options, TimeProvider.System, FieldProtector);
        dbContext.UserProfiles.Add(new UserProfile
        {
            OwnerId = "user-1",
            TimeZoneId = "UTC",
            ReminderEmailDelivery = ReminderEmailDelivery.DailyDigest,
            UnsubscribeToken = token,
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var createdProfile = await dbContext.UserProfiles.AsNoTracking().SingleAsync();
        createdProfile.UnsubscribeTokenVerifier.Should().Be(UnsubscribeTokenHash.Compute(token));

        var service = new UnsubscribeService(dbContext);

        (await service.UnsubscribeAsync(token)).Should().BeTrue();
        (await service.UnsubscribeAsync(token)).Should().BeTrue();

        var profile = await dbContext.UserProfiles.AsNoTracking().SingleAsync();
        profile.ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.None);
        profile.UnsubscribeToken.Should().Be(token);
    }

    [Fact]
    public async Task UnsubscribeAsync_with_unknown_token_returns_false()
    {
        await using var dbContext = CreateDbContext();
        dbContext.UserProfiles.Add(new UserProfile
        {
            OwnerId = "user-1",
            TimeZoneId = "UTC",
            ReminderEmailDelivery = ReminderEmailDelivery.DailyDigest,
            UnsubscribeToken = "token-123",
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = new UnsubscribeService(dbContext);
        var result = await service.UnsubscribeAsync("token-wrong");

        result.Should().BeFalse();

        var profile = await dbContext.UserProfiles.AsNoTracking().SingleAsync(p => p.OwnerId == "user-1");
        profile.ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.DailyDigest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UnsubscribeAsync_with_null_or_whitespace_token_returns_false(string? token)
    {
        await using var dbContext = CreateDbContext();
        var service = new UnsubscribeService(dbContext);

        var result = await service.UnsubscribeAsync(token!);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UnsubscribeAsync_only_modifies_matching_user_profile()
    {
        await using var dbContext = CreateDbContext();
        dbContext.UserProfiles.AddRange(
            new UserProfile
            {
                OwnerId = "user-1",
                TimeZoneId = "UTC",
                ReminderEmailDelivery = ReminderEmailDelivery.DailyDigest,
                UnsubscribeToken = "token-user-1",
            },
            new UserProfile
            {
                OwnerId = "user-2",
                TimeZoneId = "UTC",
                ReminderEmailDelivery = ReminderEmailDelivery.Immediate,
                UnsubscribeToken = "token-user-2",
            });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = new UnsubscribeService(dbContext);
        var result = await service.UnsubscribeAsync("token-user-1");

        result.Should().BeTrue();

        var p1 = await dbContext.UserProfiles.AsNoTracking().SingleAsync(p => p.OwnerId == "user-1");
        var p2 = await dbContext.UserProfiles.AsNoTracking().SingleAsync(p => p.OwnerId == "user-2");

        p1.ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.None);
        p2.ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.Immediate);
    }

    [Fact]
    public async Task UnsubscribeAsync_leaves_nothing_tracked()
    {
        await using var dbContext = CreateDbContext();
        dbContext.UserProfiles.Add(new UserProfile
        {
            OwnerId = "user-1",
            TimeZoneId = "UTC",
            ReminderEmailDelivery = ReminderEmailDelivery.DailyDigest,
            UnsubscribeToken = "token-123",
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = new UnsubscribeService(dbContext);
        await service.UnsubscribeAsync("token-123");

        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    private static RelioDbContext CreateDbContext()
    {
        return new RelioDbContext(CreateOptions(), TimeProvider.System, FieldProtector);
    }

    private static DbContextOptions<RelioDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
}
