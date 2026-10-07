using Relio.Application.People;
using static Relio.Data.Tests.People.TagServiceManagementTests;

namespace Relio.Data.Tests.People;

/// <summary>
/// Cross-user isolation for issue #25: user B can neither see, rename nor remove user A's tags
/// (see PeopleServiceOwnershipTests for the shape).
/// </summary>
public class TagServiceOwnershipTests
{
    private const string A = "user-a";
    private const string B = "user-b";

    [Fact]
    public async Task ListWithUsageAsync_only_lists_the_current_users_tags()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        await AddTagAsync(dbContext, A, "Secret");
        await AddTagAsync(dbContext, B, "Mine");

        (await CreateService(dbContext, B).ListWithUsageAsync()).Select(t => t.Name).Should().Equal("Mine");
    }

    [Fact]
    public async Task RenameAsync_for_another_users_tag_returns_false_and_keeps_its_name()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var tagOfA = await AddTagAsync(dbContext, A, "Secret");

        (await CreateService(dbContext, B).RenameAsync(tagOfA, "Mine now")).Should().BeFalse();

        (await ReadTagsAsync(database)).Should().ContainSingle().Which.Name.Should().Be("Secret");
    }

    [Fact]
    public async Task DeleteAsync_for_another_users_tag_returns_false_and_keeps_it_and_its_links()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var tagOfA = await AddTagAsync(dbContext, A, "Secret");
        var personOfA = await AddPersonAsync(dbContext, A, "Ada", tagOfA);

        (await CreateService(dbContext, B).DeleteAsync(tagOfA)).Should().BeFalse();

        (await ReadTagsAsync(database)).Should().ContainSingle();
        (await TagNamesOfAsync(database, personOfA)).Should().Equal("Secret");
    }

    [Fact]
    public async Task CreateAsync_allows_a_name_another_user_already_has()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        await AddTagAsync(dbContext, A, "Climbing");

        var created = await CreateService(dbContext, B).CreateAsync("Climbing");

        created.OwnerId.Should().Be(B);
        (await ReadTagsAsync(database)).Should().HaveCount(2);
    }

    [Fact]
    public async Task RenameAsync_allows_a_name_another_user_already_has()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        await AddTagAsync(dbContext, A, "Climbing");
        var own = await AddTagAsync(dbContext, B, "Chess");

        (await CreateService(dbContext, B).RenameAsync(own, "Climbing")).Should().BeTrue();
    }

    [Fact]
    public async Task A_refusal_for_a_foreign_tag_and_for_a_missing_one_look_the_same()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var tagOfA = await AddTagAsync(dbContext, A, "Secret");
        var service = CreateService(dbContext, B);

        (await service.RenameAsync(tagOfA, "X")).Should().Be(await service.RenameAsync(Guid.NewGuid(), "X"));
        (await service.DeleteAsync(tagOfA)).Should().Be(await service.DeleteAsync(Guid.NewGuid()));
    }
}
