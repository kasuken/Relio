using Microsoft.EntityFrameworkCore;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// Issue #25: listing, adding, renaming and removing the current user's tags. As for relationship
/// types, name clashes are proven here as the service's own check; the unique index and the race
/// are proven against SQL Server in Relio.Data.IntegrationTests.
/// </summary>
public class TagServiceManagementTests
{
    private const string Owner = "user-a";

    [Fact]
    public async Task ListWithUsageAsync_counts_people_per_tag_including_archived()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var climbing = await AddTagAsync(dbContext, Owner, "Climbing");
        var chess = await AddTagAsync(dbContext, Owner, "Chess");
        await AddTagAsync(dbContext, Owner, "Book club");
        await AddPersonAsync(dbContext, Owner, "Ada", climbing, chess);
        await AddPersonAsync(dbContext, Owner, "Bo", isArchived: true, tagIds: [climbing]);
        await AddPersonAsync(dbContext, Owner, "Cy");

        var usage = await CreateService(dbContext, Owner).ListWithUsageAsync();

        usage.Select(u => (u.Name, u.PeopleCount)).Should().Equal(("Book club", 0), ("Chess", 1), ("Climbing", 2));
        dbContext.ChangeTracker.Entries().Should().BeEmpty("reads are untracked");
    }

    [Fact]
    public async Task ListWithUsageAsync_never_counts_another_owners_people()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var tag = await AddTagAsync(dbContext, Owner, "Climbing");
        await AddPersonAsync(dbContext, Owner, "Ada", tag);
        await AddPersonAsync(dbContext, "user-b", "Bo", tag);

        (await CreateService(dbContext, Owner).ListWithUsageAsync()).Should().ContainSingle().Which.PeopleCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_normalizes_the_name()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        var created = await CreateService(dbContext, Owner).CreateAsync("  Rock   climbing ");

        created.Name.Should().Be("Rock climbing");
        created.OwnerId.Should().Be(Owner);
        (await ReadTagsAsync(database)).Should().ContainSingle().Which.Name.Should().Be("Rock climbing");
    }

    [Theory]
    [InlineData(null, LabelValidationError.NameRequired)]
    [InlineData("  ", LabelValidationError.NameRequired)]
    [InlineData("0123456789012345678901234567890123456789012345678901", LabelValidationError.NameTooLong)]
    public async Task CreateAsync_rejects_a_blank_or_too_long_name_and_saves_nothing(string? name, LabelValidationError expected)
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        var act = () => CreateService(dbContext, Owner).CreateAsync(name);

        (await act.Should().ThrowAsync<LabelValidationException>()).Which.Error.Should().Be(expected);
        (await ReadTagsAsync(database)).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_refuses_case_only_duplicates()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        await AddTagAsync(dbContext, Owner, "Climbing");

        var act = () => CreateService(dbContext, Owner).CreateAsync(" cLIMBING");

        (await act.Should().ThrowAsync<LabelValidationException>()).Which.Error.Should().Be(LabelValidationError.NameTaken);
        (await ReadTagsAsync(database)).Should().ContainSingle();
    }

    [Fact]
    public async Task RenameAsync_changes_the_tag_for_every_person_using_it()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var climbing = await AddTagAsync(dbContext, Owner, "Climbing");
        var ada = await AddPersonAsync(dbContext, Owner, "Ada", climbing);
        var bo = await AddPersonAsync(dbContext, Owner, "Bo", climbing);
        var cy = await AddPersonAsync(dbContext, Owner, "Cy");

        (await CreateService(dbContext, Owner).RenameAsync(climbing, "Bouldering")).Should().BeTrue();

        (await TagNamesOfAsync(database, ada)).Should().Equal("Bouldering");
        (await TagNamesOfAsync(database, bo)).Should().Equal("Bouldering");
        (await TagNamesOfAsync(database, cy)).Should().BeEmpty();
    }

    [Fact]
    public async Task RenameAsync_can_change_only_the_casing()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var tag = await AddTagAsync(dbContext, Owner, "chess");

        (await CreateService(dbContext, Owner).RenameAsync(tag, "Chess")).Should().BeTrue();

        (await ReadTagsAsync(database)).Should().ContainSingle().Which.Name.Should().Be("Chess");
    }

    [Fact]
    public async Task RenameAsync_to_another_tags_name_is_refused()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var chess = await AddTagAsync(dbContext, Owner, "Chess");
        await AddTagAsync(dbContext, Owner, "Climbing");

        var act = () => CreateService(dbContext, Owner).RenameAsync(chess, "climbing");

        (await act.Should().ThrowAsync<LabelValidationException>()).Which.Error.Should().Be(LabelValidationError.NameTaken);
        (await ReadTagsAsync(database)).Select(t => t.Name).Should().BeEquivalentTo("Chess", "Climbing");
    }

    [Fact]
    public async Task RenameAsync_for_a_missing_tag_returns_false()
    {
        await using var dbContext = CreateDbContext(NewDatabase());

        (await CreateService(dbContext, Owner).RenameAsync(Guid.NewGuid(), "Chess")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_takes_the_tag_off_people_and_keeps_the_people()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var climbing = await AddTagAsync(dbContext, Owner, "Climbing");
        var chess = await AddTagAsync(dbContext, Owner, "Chess");
        var ada = await AddPersonAsync(dbContext, Owner, "Ada", climbing, chess);
        var bo = await AddPersonAsync(dbContext, Owner, "Bo", isArchived: true, tagIds: [climbing]);
        var before = (await ReadPeopleAsync(database)).ToDictionary(p => p.Id, p => p.UpdatedAtUtc);

        (await CreateService(dbContext, Owner).DeleteAsync(climbing)).Should().BeTrue();

        (await ReadTagsAsync(database)).Select(t => t.Name).Should().Equal("Chess");
        (await TagNamesOfAsync(database, ada)).Should().Equal("Chess");
        (await TagNamesOfAsync(database, bo)).Should().BeEmpty();
        var after = await ReadPeopleAsync(database);
        after.Should().HaveCount(2);
        after.Should().OnlyContain(p => p.UpdatedAtUtc == before[p.Id], "the people themselves are not changed");
    }

    [Fact]
    public async Task DeleteAsync_for_a_missing_tag_returns_false()
    {
        await using var dbContext = CreateDbContext(NewDatabase());

        (await CreateService(dbContext, Owner).DeleteAsync(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task A_tag_name_rejected_in_settings_can_still_be_created_by_PeopleService_afterwards()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        await ((Func<Task>)(() => CreateService(dbContext, Owner).CreateAsync("   "))).Should().ThrowAsync<LabelValidationException>();
        var person = await new PeopleService(dbContext, new FakeCurrentUser(Owner), TimeProvider.System)
            .CreateAsync(new CreatePersonRequest { FirstName = "Ada" });

        var updated = await new PeopleService(dbContext, new FakeCurrentUser(Owner), TimeProvider.System)
            .UpdateAsync(person.Id, new UpdatePersonRequest { FirstName = "Ada", NewTagNames = ["Chess"] });

        updated.Should().BeTrue();
        (await TagNamesOfAsync(database, person.Id)).Should().Equal("Chess");
    }

    [Fact]
    public async Task Mutations_leave_nothing_tracked_on_success_and_failure()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var climbing = await AddTagAsync(dbContext, Owner, "Climbing");
        var chess = await AddTagAsync(dbContext, Owner, "Chess");
        await AddPersonAsync(dbContext, Owner, "Ada", climbing);
        var service = CreateService(dbContext, Owner);

        await service.CreateAsync("Mentor");
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await service.RenameAsync(chess, "Draughts");
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await ((Func<Task>)(() => service.CreateAsync("climbing"))).Should().ThrowAsync<LabelValidationException>();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await ((Func<Task>)(() => service.RenameAsync(chess, "Climbing"))).Should().ThrowAsync<LabelValidationException>();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await service.DeleteAsync(climbing);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task Every_method_without_an_authenticated_user_throws()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = CreateService(dbContext, null);

        await ((Func<Task>)(() => service.ListWithUsageAsync())).Should().ThrowAsync<UnauthenticatedUserException>();
        await ((Func<Task>)(() => service.CreateAsync("Chess"))).Should().ThrowAsync<UnauthenticatedUserException>();
        await ((Func<Task>)(() => service.RenameAsync(Guid.NewGuid(), "Chess"))).Should().ThrowAsync<UnauthenticatedUserException>();
        await ((Func<Task>)(() => service.DeleteAsync(Guid.NewGuid()))).Should().ThrowAsync<UnauthenticatedUserException>();
    }

    internal static DbContextOptions<RelioDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    internal static RelioDbContext CreateDbContext(DbContextOptions<RelioDbContext> database) =>
        new(database, TimeProvider.System, FieldProtector);

    internal static TagService CreateService(RelioDbContext dbContext, string? ownerId) =>
        new(dbContext, new FakeCurrentUser(ownerId));

    internal static async Task<Guid> AddTagAsync(RelioDbContext dbContext, string ownerId, string name)
    {
        var tag = new Tag { OwnerId = ownerId, Name = name };
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return tag.Id;
    }

    internal static Task<Guid> AddPersonAsync(RelioDbContext dbContext, string ownerId, string firstName, params Guid[] tagIds) =>
        AddPersonAsync(dbContext, ownerId, firstName, isArchived: false, tagIds);

    internal static async Task<Guid> AddPersonAsync(
        RelioDbContext dbContext, string ownerId, string firstName, bool isArchived, Guid[] tagIds)
    {
        var tags = await dbContext.Tags.Where(t => tagIds.Contains(t.Id)).ToListAsync();
        var person = new Person { OwnerId = ownerId, FirstName = firstName, IsArchived = isArchived };
        foreach (var tag in tags)
        {
            person.Tags.Add(tag);
        }

        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return person.Id;
    }

    internal static async Task<List<Tag>> ReadTagsAsync(DbContextOptions<RelioDbContext> database)
    {
        await using var fresh = CreateDbContext(database);
        return await fresh.Tags.AsNoTracking().OrderBy(t => t.Name).ToListAsync();
    }

    internal static async Task<List<Person>> ReadPeopleAsync(DbContextOptions<RelioDbContext> database)
    {
        await using var fresh = CreateDbContext(database);
        return await fresh.People.AsNoTracking().ToListAsync();
    }

    internal static async Task<List<string>> TagNamesOfAsync(DbContextOptions<RelioDbContext> database, Guid personId)
    {
        await using var fresh = CreateDbContext(database);
        var person = await fresh.People.AsNoTracking().Include(p => p.Tags).SingleAsync(p => p.Id == personId);
        return person.Tags.Select(t => t.Name).OrderBy(name => name, StringComparer.Ordinal).ToList();
    }
}
