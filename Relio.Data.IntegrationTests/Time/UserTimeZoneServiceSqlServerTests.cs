using Microsoft.EntityFrameworkCore;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Time;

namespace Relio.Data.IntegrationTests.Time;

/// <summary>
/// Re-proves, against a real SQL Server database, the user-profile scenarios from
/// <c>Relio.Data.Tests.Time.UserTimeZoneServiceTests</c> (which uses the EF Core InMemory
/// provider): the unique <c>(OwnerId)</c> index actually enforces one profile per user, the
/// <c>AddUserProfile</c> migration applies cleanly, and user A cannot read or overwrite user B's
/// time zone.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class UserTimeZoneServiceSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task GetTimeZoneAsync_defaults_to_UTC_when_no_profile_exists()
    {
        await using var dbContext = fixture.CreateDbContext();
        var service = CreateService(dbContext, TestDataFactory.NewOwnerId());

        var timeZone = await service.GetTimeZoneAsync();

        timeZone.Id.Should().Be("UTC");
    }

    [SqlServerFact]
    public async Task SetTimeZoneAsync_then_GetTimeZoneAsync_round_trips_through_the_database()
    {
        await using var dbContext = fixture.CreateDbContext();
        var service = CreateService(dbContext, TestDataFactory.NewOwnerId());

        await service.SetTimeZoneAsync("Europe/Rome");

        (await service.GetTimeZoneAsync()).Id.Should().Be("Europe/Rome");
    }

    [SqlServerFact]
    public async Task SetTimeZoneAsync_with_an_unknown_id_throws_and_creates_no_profile()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerId = TestDataFactory.NewOwnerId();
        var service = CreateService(dbContext, ownerId);

        var act = () => service.SetTimeZoneAsync("Not/AZone");

        await act.Should().ThrowAsync<InvalidTimeZoneIdException>();
        (await dbContext.UserProfiles.CountAsync(p => p.OwnerId == ownerId)).Should().Be(0);
    }

    [SqlServerFact]
    public async Task Operations_without_an_authenticated_user_throw()
    {
        await using var dbContext = fixture.CreateDbContext();
        var service = CreateService(dbContext, ownerId: null);

        var act = () => service.GetTimeZoneAsync();

        await act.Should().ThrowAsync<UnauthenticatedUserException>();
    }

    [SqlServerFact]
    public async Task User_A_cannot_read_or_overwrite_user_Bs_time_zone()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        await CreateService(dbContext, ownerB).SetTimeZoneAsync("Pacific/Kiritimati");

        var serviceForA = CreateService(dbContext, ownerA);
        var timeZoneForA = await serviceForA.GetTimeZoneAsync();
        timeZoneForA.Id.Should().Be("UTC");

        await serviceForA.SetTimeZoneAsync("Pacific/Pago_Pago");

        var profileForB = await dbContext.UserProfiles.AsNoTracking().SingleAsync(p => p.OwnerId == ownerB);
        profileForB.TimeZoneId.Should().Be("Pacific/Kiritimati");
    }

    private static UserTimeZoneService CreateService(RelioDbContext dbContext, string? ownerId) =>
        new(dbContext, new FakeCurrentUser(ownerId), TimeProvider.System);
}
