using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Relio.Application.Onboarding;
using Relio.Application.Security;
using Relio.Data.Onboarding;
using Relio.Data.Tests.People;
using Relio.Domain;

namespace Relio.Data.Tests.Onboarding;

public sealed class OnboardingServiceTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    [Fact]
    public async Task New_profile_default_and_missing_profile_are_dismissed()
    {
        var profile = new UserProfile { OwnerId = UserA };
        profile.OnboardingDismissed.Should().BeTrue();

        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, UserA);

        (await service.GetStateAsync()).IsPending.Should().BeFalse();

        await service.DismissAsync();

        (await dbContext.UserProfiles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task State_and_dismissal_are_scoped_to_the_current_user()
    {
        await using var dbContext = CreateDbContext();
        dbContext.UserProfiles.AddRange(
            new UserProfile { OwnerId = UserA },
            new UserProfile { OwnerId = UserB, OnboardingDismissed = false });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var serviceForA = CreateService(dbContext, UserA);
        var serviceForB = CreateService(dbContext, UserB);

        (await serviceForA.GetStateAsync()).IsPending.Should().BeFalse();
        (await serviceForB.GetStateAsync()).IsPending.Should().BeTrue();

        await serviceForA.DismissAsync();

        (await serviceForA.GetStateAsync()).IsPending.Should().BeFalse();
        (await serviceForB.GetStateAsync()).IsPending.Should().BeTrue();
        (await dbContext.UserProfiles.AsNoTracking()
            .SingleAsync(profile => profile.OwnerId == UserB)).OnboardingDismissed.Should().BeFalse();
    }

    [Fact]
    public async Task Dismissal_clears_the_change_tracker_when_saving_fails()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        var seedOptions = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;

        await using (var seed = new RelioDbContext(seedOptions, TimeProvider.System))
        {
            seed.UserProfiles.Add(new UserProfile { OwnerId = UserA, OnboardingDismissed = false });
            await seed.SaveChangesAsync();
        }

        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .AddInterceptors(new FailOnceSaveChangesInterceptor())
            .Options;
        await using var dbContext = new RelioDbContext(options, TimeProvider.System);
        var service = CreateService(dbContext, UserA);

        var act = () => service.DismissAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await service.DismissAsync();

        (await service.GetStateAsync()).IsPending.Should().BeFalse();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task Every_method_requires_an_authenticated_user()
    {
        await using var dbContext = CreateDbContext();
        var service = new OnboardingService(dbContext, new FakeCurrentUser(null));

        var read = () => service.GetStateAsync();
        var dismiss = () => service.DismissAsync();

        await read.Should().ThrowAsync<UnauthenticatedUserException>();
        await dismiss.Should().ThrowAsync<UnauthenticatedUserException>();
    }

    private static RelioDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RelioDbContext(options, TimeProvider.System);
    }

    private static IOnboardingService CreateService(RelioDbContext dbContext, string userId) =>
        new OnboardingService(dbContext, new FakeCurrentUser(userId));
}
