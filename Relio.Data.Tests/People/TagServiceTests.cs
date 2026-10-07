using Microsoft.EntityFrameworkCore;
using Relio.Application.Security;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Tests.People;

public class TagServiceTests
{
    [Fact]
    public async Task ListAsync_returns_only_the_current_users_tags_ordered_by_name()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Tags.AddRange(
            new Tag { OwnerId = "user-a", Name = "Work" },
            new Tag { OwnerId = "user-b", Name = "Secret" },
            new Tag { OwnerId = "user-a", Name = "Chess" },
            new Tag { OwnerId = "user-a", Name = "Mentor" });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var tags = await new TagService(dbContext, new FakeCurrentUser("user-a")).ListAsync();

        tags.Select(t => t.Name).Should().Equal("Chess", "Mentor", "Work");
        tags.Should().OnlyContain(t => t.OwnerId == "user-a");
        dbContext.ChangeTracker.Entries().Should().BeEmpty("reads are untracked");
    }

    [Fact]
    public async Task ListAsync_for_a_user_with_no_tags_is_empty_even_when_others_have_some()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Tags.Add(new Tag { OwnerId = "user-a", Name = "Work" });
        await dbContext.SaveChangesAsync();

        (await new TagService(dbContext, new FakeCurrentUser("user-b")).ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_without_an_authenticated_user_throws()
    {
        await using var dbContext = CreateDbContext();

        var act = () => new TagService(dbContext, new FakeCurrentUser(null)).ListAsync();

        await act.Should().ThrowAsync<UnauthenticatedUserException>();
    }

    private static RelioDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RelioDbContext(options, TimeProvider.System);
    }
}
