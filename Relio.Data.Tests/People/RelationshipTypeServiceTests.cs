using Microsoft.EntityFrameworkCore;
using Relio.Application.Security;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// <see cref="RelationshipTypeService"/> and the defaults every account starts with: the list is
/// the current user's own, in the order the user picks from, and never anyone else's.
/// </summary>
public class RelationshipTypeServiceTests
{
    [Fact]
    public async Task ListAsync_returns_only_the_current_users_types_in_sort_order()
    {
        await using var dbContext = CreateDbContext();
        dbContext.RelationshipTypes.AddRange(
            new RelationshipType { OwnerId = "user-a", Name = "Friend", SortOrder = 2 },
            new RelationshipType { OwnerId = "user-a", Name = "Family", SortOrder = 0 },
            new RelationshipType { OwnerId = "user-a", Name = "Colleague", SortOrder = 2 },
            new RelationshipType { OwnerId = "user-b", Name = "Rival", SortOrder = 0 });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var types = await new RelationshipTypeService(dbContext, new FakeCurrentUser("user-a")).ListAsync();

        // Same sort order falls back to the name.
        types.Select(t => t.Name).Should().Equal("Family", "Colleague", "Friend");
        types.Should().OnlyContain(t => t.OwnerId == "user-a");
        dbContext.ChangeTracker.Entries().Should().BeEmpty("reads are untracked");
    }

    [Fact]
    public async Task ListAsync_without_an_authenticated_user_throws()
    {
        await using var dbContext = CreateDbContext();
        var service = new RelationshipTypeService(dbContext, new FakeCurrentUser(null));

        var act = () => service.ListAsync();

        await act.Should().ThrowAsync<UnauthenticatedUserException>();
    }

    [Fact]
    public void CreateDefaults_returns_the_six_defaults_in_order_owned_by_the_user()
    {
        var defaults = RelationshipType.CreateDefaults("user-a");

        defaults.Select(t => t.Name).Should().Equal("Family", "Partner", "Friend", "Colleague", "Acquaintance", "Other");
        defaults.Select(t => t.SortOrder).Should().Equal(0, 1, 2, 3, 4, 5);
        defaults.Should().OnlyContain(t => t.OwnerId == "user-a");
        defaults.Select(t => t.Id).Should().OnlyHaveUniqueItems();
    }

    private static RelioDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RelioDbContext(options, TimeProvider.System);
    }
}
