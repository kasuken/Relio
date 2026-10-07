using Relio.Application.Ownership;
using Relio.Application.People;
using static Relio.Data.Tests.People.RelationshipTypeServiceManagementTests;

namespace Relio.Data.Tests.People;

/// <summary>
/// Cross-user isolation for issue #25: user B can neither see, rename nor remove user A's
/// relationship types, nor move people onto them (see PeopleServiceOwnershipTests for the shape).
/// </summary>
public class RelationshipTypeServiceOwnershipTests
{
    private const string A = "user-a";
    private const string B = "user-b";

    [Fact]
    public async Task ListWithUsageAsync_only_lists_the_current_users_types()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        await AddTypeAsync(dbContext, A, "Mentor", 0);
        await AddTypeAsync(dbContext, B, "Rival", 0);

        (await CreateService(dbContext, B).ListWithUsageAsync()).Select(t => t.Name).Should().Equal("Rival");
        (await CreateService(dbContext, A).ListWithUsageAsync()).Select(t => t.Name).Should().Equal("Mentor");
    }

    [Fact]
    public async Task RenameAsync_for_another_users_type_returns_false_and_keeps_its_name()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var typeOfA = await AddTypeAsync(dbContext, A, "Mentor", 0);

        (await CreateService(dbContext, B).RenameAsync(typeOfA, "Mine now")).Should().BeFalse();

        (await ReadTypesAsync(database)).Should().ContainSingle().Which.Name.Should().Be("Mentor");
    }

    [Fact]
    public async Task DeleteAsync_for_another_users_type_returns_false_and_keeps_it_and_its_people()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var typeOfA = await AddTypeAsync(dbContext, A, "Mentor", 0);
        var personOfA = await AddPersonAsync(dbContext, A, "Ada", typeOfA);
        var ownTypeOfB = await AddTypeAsync(dbContext, B, "Rival", 0);

        (await CreateService(dbContext, B).DeleteAsync(typeOfA, ownTypeOfB)).Should().BeFalse();

        (await ReadTypesAsync(database)).Select(t => t.Name).Should().BeEquivalentTo("Mentor", "Rival");
        (await ReadPeopleAsync(database)).Single(p => p.Id == personOfA).RelationshipTypeId.Should().Be(typeOfA);
    }

    [Fact]
    public async Task DeleteAsync_cannot_move_people_to_another_users_type()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var ownType = await AddTypeAsync(dbContext, B, "Friend", 0);
        var typeOfA = await AddTypeAsync(dbContext, A, "Mentor", 0);
        var person = await AddPersonAsync(dbContext, B, "Bo", ownType);

        var act = () => CreateService(dbContext, B).DeleteAsync(ownType, typeOfA);

        await act.Should().ThrowAsync<ForeignEntityNotOwnedException>();
        (await ReadTypesAsync(database)).Should().HaveCount(2);
        (await ReadPeopleAsync(database)).Single(p => p.Id == person).RelationshipTypeId.Should().Be(ownType);
    }

    [Fact]
    public async Task A_nonexistent_and_a_foreign_target_give_the_same_message()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var ownType = await AddTypeAsync(dbContext, B, "Friend", 0);
        var typeOfA = await AddTypeAsync(dbContext, A, "Mentor", 0);
        var service = CreateService(dbContext, B);

        var foreign = await ((Func<Task>)(() => service.DeleteAsync(ownType, typeOfA))).Should().ThrowAsync<ForeignEntityNotOwnedException>();
        var missing = await ((Func<Task>)(() => service.DeleteAsync(ownType, Guid.NewGuid()))).Should().ThrowAsync<ForeignEntityNotOwnedException>();

        foreign.Which.Message.Should().Be(missing.Which.Message);
    }

    [Fact]
    public async Task CreateAsync_allows_a_name_another_user_already_has()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        await AddTypeAsync(dbContext, A, "Mentor", 0);

        var created = await CreateService(dbContext, B).CreateAsync("Mentor");

        created.OwnerId.Should().Be(B);
        created.SortOrder.Should().Be(0, "B's list is empty, A's types do not count");
        (await ReadTypesAsync(database)).Should().HaveCount(2);
    }

    [Fact]
    public async Task RenameAsync_allows_a_name_another_user_already_has()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        await AddTypeAsync(dbContext, A, "Mentor", 0);
        var own = await AddTypeAsync(dbContext, B, "Friend", 0);

        (await CreateService(dbContext, B).RenameAsync(own, "Mentor")).Should().BeTrue();
    }

    [Fact]
    public async Task A_refusal_for_a_foreign_type_and_for_a_missing_one_look_the_same()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var typeOfA = await AddTypeAsync(dbContext, A, "Mentor", 0);
        var service = CreateService(dbContext, B);

        (await service.RenameAsync(typeOfA, "X")).Should().Be(await service.RenameAsync(Guid.NewGuid(), "X"));
        (await service.DeleteAsync(typeOfA, null)).Should().Be(await service.DeleteAsync(Guid.NewGuid(), null));
    }
}
