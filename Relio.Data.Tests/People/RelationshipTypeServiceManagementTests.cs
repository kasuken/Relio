using Microsoft.EntityFrameworkCore;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// Issue #25: listing, adding, renaming and removing the current user's relationship types. The
/// InMemory provider is case-sensitive and has no unique index, so name clashes are proven here as
/// the service's own check; the index and the race are proven against SQL Server in
/// Relio.Data.IntegrationTests.
/// </summary>
public class RelationshipTypeServiceManagementTests
{
    private const string Owner = "user-a";

    [Fact]
    public async Task ListWithUsageAsync_counts_people_per_type_including_archived()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var friend = await AddTypeAsync(dbContext, Owner, "Friend", 0);
        var family = await AddTypeAsync(dbContext, Owner, "Family", 1);
        await AddTypeAsync(dbContext, Owner, "Other", 2);
        await AddPersonAsync(dbContext, Owner, "Ada", friend);
        await AddPersonAsync(dbContext, Owner, "Bo", friend, isArchived: true);
        await AddPersonAsync(dbContext, Owner, "Cy", family);
        await AddPersonAsync(dbContext, Owner, "Di", relationshipTypeId: null);

        var usage = await CreateService(dbContext, Owner).ListWithUsageAsync();

        usage.Select(u => (u.Name, u.PeopleCount)).Should().Equal(("Friend", 2), ("Family", 1), ("Other", 0));
        usage.Select(u => u.SortOrder).Should().Equal(0, 1, 2);
        dbContext.ChangeTracker.Entries().Should().BeEmpty("reads are untracked");
    }

    [Fact]
    public async Task ListWithUsageAsync_never_counts_another_owners_people()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var mine = await AddTypeAsync(dbContext, Owner, "Friend", 0);
        await AddPersonAsync(dbContext, Owner, "Ada", mine);
        // A person of someone else pointing at my type id: it must not be counted.
        await AddPersonAsync(dbContext, "user-b", "Bo", mine);

        var usage = await CreateService(dbContext, Owner).ListWithUsageAsync();

        usage.Should().ContainSingle().Which.PeopleCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_normalizes_the_name_and_appends_it_last()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        foreach (var (name, index) in RelationshipType.DefaultNames.Select((n, i) => (n, i)))
        {
            await AddTypeAsync(dbContext, Owner, name, index);
        }

        var created = await CreateService(dbContext, Owner).CreateAsync("  Book   club ");

        created.Name.Should().Be("Book club");
        created.SortOrder.Should().Be(6);
        created.OwnerId.Should().Be(Owner);
        (await ReadTypesAsync(database)).Select(t => t.Name).Should().EndWith("Book club");
    }

    [Fact]
    public async Task CreateAsync_on_an_empty_list_starts_at_zero()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        var created = await CreateService(dbContext, Owner).CreateAsync("Mentor");

        created.SortOrder.Should().Be(0);
    }

    [Theory]
    [InlineData(null, LabelValidationError.NameRequired)]
    [InlineData("", LabelValidationError.NameRequired)]
    [InlineData("   ", LabelValidationError.NameRequired)]
    [InlineData("0123456789012345678901234567890123456789012345678901", LabelValidationError.NameTooLong)]
    public async Task CreateAsync_rejects_a_blank_or_too_long_name_and_saves_nothing(string? name, LabelValidationError expected)
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        var act = () => CreateService(dbContext, Owner).CreateAsync(name);

        (await act.Should().ThrowAsync<LabelValidationException>()).Which.Error.Should().Be(expected);
        (await ReadTypesAsync(database)).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_accepts_a_name_of_exactly_the_longest_length()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        var created = await CreateService(dbContext, Owner).CreateAsync(new string('x', RelationshipType.NameMaxLength));

        created.Name.Should().HaveLength(RelationshipType.NameMaxLength);
    }

    [Fact]
    public async Task CreateAsync_refuses_a_name_that_differs_only_in_case()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        await AddTypeAsync(dbContext, Owner, "Friend", 0);

        var act = () => CreateService(dbContext, Owner).CreateAsync("fRIEND ");

        (await act.Should().ThrowAsync<LabelValidationException>()).Which.Error.Should().Be(LabelValidationError.NameTaken);
        (await ReadTypesAsync(database)).Should().ContainSingle();
    }

    [Fact]
    public async Task RenameAsync_changes_the_name_people_see()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var friend = await AddTypeAsync(dbContext, Owner, "Friend", 0);
        var personId = await AddPersonAsync(dbContext, Owner, "Ada", friend);

        var renamed = await CreateService(dbContext, Owner).RenameAsync(friend, "  Close   friend ");

        renamed.Should().BeTrue();
        await using var fresh = CreateDbContext(database);
        var person = await fresh.People.AsNoTracking().Include(p => p.RelationshipType).SingleAsync(p => p.Id == personId);
        person.RelationshipType!.Name.Should().Be("Close friend");
        person.RelationshipTypeId.Should().Be(friend, "people refer to the type by id");
    }

    [Fact]
    public async Task RenameAsync_can_change_only_the_casing()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var friend = await AddTypeAsync(dbContext, Owner, "friend", 0);

        (await CreateService(dbContext, Owner).RenameAsync(friend, "Friend")).Should().BeTrue();

        (await ReadTypesAsync(database)).Should().ContainSingle().Which.Name.Should().Be("Friend");
    }

    [Fact]
    public async Task RenameAsync_to_the_same_name_succeeds_without_changing_anything()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var friend = await AddTypeAsync(dbContext, Owner, "Friend", 0);

        (await CreateService(dbContext, Owner).RenameAsync(friend, " Friend ")).Should().BeTrue();

        (await ReadTypesAsync(database)).Should().ContainSingle().Which.Name.Should().Be("Friend");
    }

    [Fact]
    public async Task RenameAsync_to_another_types_name_is_refused_and_changes_nothing()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var friend = await AddTypeAsync(dbContext, Owner, "Friend", 0);
        await AddTypeAsync(dbContext, Owner, "Colleague", 1);

        var act = () => CreateService(dbContext, Owner).RenameAsync(friend, "colleague");

        (await act.Should().ThrowAsync<LabelValidationException>()).Which.Error.Should().Be(LabelValidationError.NameTaken);
        (await ReadTypesAsync(database)).Select(t => t.Name).Should().BeEquivalentTo("Friend", "Colleague");
    }

    [Theory]
    [InlineData("", LabelValidationError.NameRequired)]
    [InlineData("0123456789012345678901234567890123456789012345678901", LabelValidationError.NameTooLong)]
    public async Task RenameAsync_rejects_a_blank_or_too_long_name_before_looking_anything_up(string name, LabelValidationError expected)
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        // The id does not exist, and the answer is still the validation error: request-only rules come first.
        var act = () => CreateService(dbContext, Owner).RenameAsync(Guid.NewGuid(), name);

        (await act.Should().ThrowAsync<LabelValidationException>()).Which.Error.Should().Be(expected);
    }

    [Fact]
    public async Task RenameAsync_for_a_missing_type_returns_false()
    {
        await using var dbContext = CreateDbContext(NewDatabase());

        (await CreateService(dbContext, Owner).RenameAsync(Guid.NewGuid(), "Mentor")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_without_people_removes_the_type()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var friend = await AddTypeAsync(dbContext, Owner, "Friend", 0);
        await AddTypeAsync(dbContext, Owner, "Family", 1);

        (await CreateService(dbContext, Owner).DeleteAsync(friend, null)).Should().BeTrue();

        (await ReadTypesAsync(database)).Select(t => t.Name).Should().Equal("Family");
    }

    [Fact]
    public async Task DeleteAsync_moves_every_person_to_the_chosen_type_including_archived()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var friend = await AddTypeAsync(dbContext, Owner, "Friend", 0);
        var acquaintance = await AddTypeAsync(dbContext, Owner, "Acquaintance", 1);
        var ada = await AddPersonAsync(dbContext, Owner, "Ada", friend);
        var bo = await AddPersonAsync(dbContext, Owner, "Bo", friend, isArchived: true);

        (await CreateService(dbContext, Owner).DeleteAsync(friend, acquaintance)).Should().BeTrue();

        (await ReadTypesAsync(database)).Select(t => t.Id).Should().Equal(acquaintance);
        var people = await ReadPeopleAsync(database);
        people.Single(p => p.Id == ada).RelationshipTypeId.Should().Be(acquaintance);
        people.Single(p => p.Id == bo).RelationshipTypeId.Should().Be(acquaintance);
        people.Single(p => p.Id == bo).IsArchived.Should().BeTrue("archiving is untouched");
    }

    [Fact]
    public async Task DeleteAsync_without_a_target_leaves_people_without_a_type()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var friend = await AddTypeAsync(dbContext, Owner, "Friend", 0);
        var ada = await AddPersonAsync(dbContext, Owner, "Ada", friend);

        (await CreateService(dbContext, Owner).DeleteAsync(friend, null)).Should().BeTrue();

        var person = (await ReadPeopleAsync(database)).Single(p => p.Id == ada);
        person.RelationshipTypeId.Should().BeNull();
        person.FirstName.Should().Be("Ada", "nothing else about the person changes");
    }

    [Fact]
    public async Task DeleteAsync_leaves_people_with_other_types_alone()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var friend = await AddTypeAsync(dbContext, Owner, "Friend", 0);
        var family = await AddTypeAsync(dbContext, Owner, "Family", 1);
        var ada = await AddPersonAsync(dbContext, Owner, "Ada", family);
        var bo = await AddPersonAsync(dbContext, Owner, "Bo", relationshipTypeId: null);

        await CreateService(dbContext, Owner).DeleteAsync(friend, null);

        var people = await ReadPeopleAsync(database);
        people.Single(p => p.Id == ada).RelationshipTypeId.Should().Be(family);
        people.Single(p => p.Id == bo).RelationshipTypeId.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_rejects_reassigning_to_the_type_being_removed()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var friend = await AddTypeAsync(dbContext, Owner, "Friend", 0);
        await AddPersonAsync(dbContext, Owner, "Ada", friend);

        var act = () => CreateService(dbContext, Owner).DeleteAsync(friend, friend);

        await act.Should().ThrowAsync<ArgumentException>();
        (await ReadTypesAsync(database)).Should().ContainSingle();
        (await ReadPeopleAsync(database)).Single().RelationshipTypeId.Should().Be(friend);
    }

    [Fact]
    public async Task DeleteAsync_with_a_nonexistent_target_throws_and_changes_nothing()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var friend = await AddTypeAsync(dbContext, Owner, "Friend", 0);
        await AddPersonAsync(dbContext, Owner, "Ada", friend);

        var act = () => CreateService(dbContext, Owner).DeleteAsync(friend, Guid.NewGuid());

        (await act.Should().ThrowAsync<ForeignEntityNotOwnedException>())
            .Which.EntityName.Should().Be(ForeignEntityNames.RelationshipTypes);
        (await ReadTypesAsync(database)).Should().ContainSingle();
        (await ReadPeopleAsync(database)).Single().RelationshipTypeId.Should().Be(friend);
    }

    [Fact]
    public async Task DeleteAsync_can_remove_the_last_type()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var only = await AddTypeAsync(dbContext, Owner, "Friend", 0);

        (await CreateService(dbContext, Owner).DeleteAsync(only, null)).Should().BeTrue();

        (await ReadTypesAsync(database)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_for_a_missing_type_returns_false()
    {
        await using var dbContext = CreateDbContext(NewDatabase());

        (await CreateService(dbContext, Owner).DeleteAsync(Guid.NewGuid(), null)).Should().BeFalse();
    }

    [Fact]
    public async Task Removing_every_type_never_brings_the_defaults_back()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        foreach (var (name, index) in RelationshipType.DefaultNames.Select((n, i) => (n, i)))
        {
            await AddTypeAsync(dbContext, Owner, name, index);
        }

        var service = CreateService(dbContext, Owner);
        foreach (var type in await service.ListAsync())
        {
            await service.DeleteAsync(type.Id, null);
        }

        (await service.ListAsync()).Should().BeEmpty();
        (await service.ListWithUsageAsync()).Should().BeEmpty();
        (await ReadTypesAsync(database)).Should().BeEmpty("no read ever creates the defaults");
    }

    [Fact]
    public async Task Mutations_leave_nothing_tracked_on_success_and_failure()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var friend = await AddTypeAsync(dbContext, Owner, "Friend", 0);
        var family = await AddTypeAsync(dbContext, Owner, "Family", 1);
        await AddPersonAsync(dbContext, Owner, "Ada", friend);
        var service = CreateService(dbContext, Owner);

        await service.CreateAsync("Mentor");
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await service.RenameAsync(family, "Relatives");
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await ((Func<Task>)(() => service.CreateAsync("friend"))).Should().ThrowAsync<LabelValidationException>();
        dbContext.ChangeTracker.Entries().Should().BeEmpty("a refused name leaves nothing for the next save");

        await ((Func<Task>)(() => service.RenameAsync(family, "Friend"))).Should().ThrowAsync<LabelValidationException>();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await ((Func<Task>)(() => service.DeleteAsync(friend, Guid.NewGuid()))).Should().ThrowAsync<ForeignEntityNotOwnedException>();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await service.DeleteAsync(friend, family);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_save_leaves_nothing_tracked_for_the_next_save()
    {
        var database = NewDatabase();
        await using var dbContext = new RelioDbContext(
            new DbContextOptionsBuilder<RelioDbContext>(database).AddInterceptors(new FailOnceSaveChangesInterceptor()).Options,
            TimeProvider.System);

        var act = () => CreateService(dbContext, Owner).CreateAsync("Mentor");
        await act.Should().ThrowAsync<InvalidOperationException>();

        dbContext.ChangeTracker.Entries().Should().BeEmpty();
        (await CreateService(dbContext, Owner).CreateAsync("Other")).Name.Should().Be("Other");
        (await ReadTypesAsync(database)).Should().ContainSingle("the failed Mentor was not inserted by the later save")
            .Which.Name.Should().Be("Other");
    }

    [Fact]
    public async Task Every_method_without_an_authenticated_user_throws()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = CreateService(dbContext, null);

        await ((Func<Task>)(() => service.ListAsync())).Should().ThrowAsync<UnauthenticatedUserException>();
        await ((Func<Task>)(() => service.ListWithUsageAsync())).Should().ThrowAsync<UnauthenticatedUserException>();
        await ((Func<Task>)(() => service.CreateAsync("Mentor"))).Should().ThrowAsync<UnauthenticatedUserException>();
        await ((Func<Task>)(() => service.RenameAsync(Guid.NewGuid(), "Mentor"))).Should().ThrowAsync<UnauthenticatedUserException>();
        await ((Func<Task>)(() => service.DeleteAsync(Guid.NewGuid(), null))).Should().ThrowAsync<UnauthenticatedUserException>();
    }

    [Fact]
    public void LabelValidationException_message_names_the_code_but_never_a_name()
    {
        var exception = new LabelValidationException(LabelValidationError.NameTaken);

        exception.Message.Should().Contain("NameTaken");
        exception.InnerException.Should().BeNull();
    }

    internal static DbContextOptions<RelioDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    internal static RelioDbContext CreateDbContext(DbContextOptions<RelioDbContext> database) =>
        new(database, TimeProvider.System);

    internal static RelationshipTypeService CreateService(RelioDbContext dbContext, string? ownerId) =>
        new(dbContext, new FakeCurrentUser(ownerId));

    internal static async Task<Guid> AddTypeAsync(RelioDbContext dbContext, string ownerId, string name, int sortOrder)
    {
        var type = new RelationshipType { OwnerId = ownerId, Name = name, SortOrder = sortOrder };
        dbContext.RelationshipTypes.Add(type);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return type.Id;
    }

    internal static async Task<Guid> AddPersonAsync(
        RelioDbContext dbContext, string ownerId, string firstName, Guid? relationshipTypeId, bool isArchived = false)
    {
        var person = new Person
        {
            OwnerId = ownerId,
            FirstName = firstName,
            RelationshipTypeId = relationshipTypeId,
            IsArchived = isArchived,
        };
        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return person.Id;
    }

    internal static async Task<List<RelationshipType>> ReadTypesAsync(DbContextOptions<RelioDbContext> database)
    {
        await using var fresh = CreateDbContext(database);
        return await fresh.RelationshipTypes.AsNoTracking().OrderBy(t => t.SortOrder).ToListAsync();
    }

    internal static async Task<List<Person>> ReadPeopleAsync(DbContextOptions<RelioDbContext> database)
    {
        await using var fresh = CreateDbContext(database);
        return await fresh.People.AsNoTracking().ToListAsync();
    }
}
