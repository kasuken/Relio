using Microsoft.EntityFrameworkCore;
using Relio.Application.Time;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Profile;
using Relio.Data.Time;

namespace Relio.Data.IntegrationTests.Profile;

/// <summary>
/// Re-proves, against a real SQL Server database, the display name scenarios from
/// <c>Relio.Data.Tests.Profile.UserProfileServiceTests</c> (which uses the EF Core InMemory
/// provider): the <c>AddUserProfileDisplayName</c> migration applies cleanly and the column
/// round-trips, and user A cannot overwrite user B's display name.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class UserProfileServiceSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Display_name_round_trips_through_the_real_migrations()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerId = await TestDataFactory.CreateOwnerAsync(fixture);
        await new UserTimeZoneService(dbContext, new FakeCurrentUser(ownerId), TimeProvider.System)
            .SetTimeZoneAsync("Europe/Rome");
        var service = CreateService(dbContext, ownerId);

        await service.SetDisplayNameAsync("  Emanuele  ");

        (await service.GetDisplayNameAsync()).Should().Be("Emanuele");
        var profile = await dbContext.UserProfiles.AsNoTracking().SingleAsync(p => p.OwnerId == ownerId);
        profile.TimeZoneId.Should().Be("Europe/Rome");
    }

    [SqlServerFact]
    public async Task User_A_cannot_overwrite_user_Bs_display_name()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = await TestDataFactory.CreateOwnerAsync(fixture);
        var ownerB = await TestDataFactory.CreateOwnerAsync(fixture);
        await CreateService(dbContext, ownerB).SetDisplayNameAsync("Bea");

        await CreateService(dbContext, ownerA).SetDisplayNameAsync("Alice");

        (await CreateService(dbContext, ownerB).GetDisplayNameAsync()).Should().Be("Bea");
        (await CreateService(dbContext, ownerA).GetDisplayNameAsync()).Should().Be("Alice");
    }

    private static UserProfileService CreateService(RelioDbContext dbContext, string? ownerId) =>
        new(dbContext, new FakeCurrentUser(ownerId));
}
