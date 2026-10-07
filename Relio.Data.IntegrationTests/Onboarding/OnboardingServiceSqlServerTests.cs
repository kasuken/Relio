using Microsoft.EntityFrameworkCore;
using Relio.Application.Onboarding;
using Relio.Application.Security;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Onboarding;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Onboarding;

/// <summary>
/// Re-proves onboarding's per-user status and persistent dismissal against the real SQL Server
/// schema created by the migrations.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class OnboardingServiceSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Pending_state_and_dismissal_are_persistent_and_owner_scoped()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = await TestDataFactory.CreateOwnerAsync(fixture);
        var ownerB = await TestDataFactory.CreateOwnerAsync(fixture);
        var ownerWithoutProfile = await TestDataFactory.CreateOwnerAsync(fixture);
        dbContext.UserProfiles.AddRange(
            new UserProfile { OwnerId = ownerA, OnboardingDismissed = false },
            new UserProfile { OwnerId = ownerB });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var serviceForA = CreateService(dbContext, ownerA);
        var serviceForB = CreateService(dbContext, ownerB);
        var serviceWithoutProfile = CreateService(dbContext, ownerWithoutProfile);

        (await serviceForA.GetStateAsync()).IsPending.Should().BeTrue();
        (await serviceForB.GetStateAsync()).IsPending.Should().BeFalse();
        (await serviceWithoutProfile.GetStateAsync()).IsPending.Should().BeFalse();

        await serviceForA.DismissAsync();

        (await serviceForA.GetStateAsync()).IsPending.Should().BeFalse();
        (await serviceForB.GetStateAsync()).IsPending.Should().BeFalse();
        await serviceWithoutProfile.DismissAsync();
        (await dbContext.UserProfiles.CountAsync(profile => profile.OwnerId == ownerWithoutProfile))
            .Should().Be(0);
        (await dbContext.UserProfiles.AsNoTracking()
            .SingleAsync(profile => profile.OwnerId == ownerA)).OnboardingDismissed.Should().BeTrue();
        (await dbContext.UserProfiles.AsNoTracking()
            .SingleAsync(profile => profile.OwnerId == ownerB)).OnboardingDismissed.Should().BeTrue();
    }

    private static IOnboardingService CreateService(RelioDbContext dbContext, string userId) =>
        new OnboardingService(dbContext, new IntegrationCurrentUser(userId));

    private sealed class IntegrationCurrentUser(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => userId;
    }
}
