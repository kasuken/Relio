using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// Issue #24: tags typed into the person form are matched against the user's own tags
/// case-insensitively, in code (the InMemory provider is case-sensitive, so the database cannot do
/// it), and tags that do not exist yet are created in the same save as the person.
/// </summary>
public class PeopleServiceTagTests
{
    private const string Owner = "owner-1";

    private static readonly DateTimeOffset Now = new(2026, 10, 6, 11, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpdateAsync_attaches_an_existing_tag_when_a_new_name_matches_it_in_another_case()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var existingId = await AddTagAsync(dbContext, "Climbing");
        var person = await CreateService(dbContext).CreateAsync(new CreatePersonRequest { FirstName = "Ada" });

        await CreateService(dbContext).UpdateAsync(person.Id, new UpdatePersonRequest { FirstName = "Ada", NewTagNames = ["cLIMBING"] });

        var tags = await ReadTagsAsync(database);
        tags.Should().ContainSingle("no second tag is created for the same name").Which.Id.Should().Be(existingId);
        (await TagNamesOfAsync(database, person.Id)).Should().Equal("Climbing");
    }

    [Fact]
    public async Task UpdateAsync_creates_new_tags_in_the_same_save_with_the_typed_casing()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var person = await CreateService(dbContext).CreateAsync(new CreatePersonRequest { FirstName = "Ada" });

        await CreateService(dbContext).UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "Ada",
            NewTagNames = ["  Rock   climbing ", "Chess"],
        });

        (await ReadTagsAsync(database)).Select(t => t.Name).Should().BeEquivalentTo("Rock climbing", "Chess");
        (await ReadTagsAsync(database)).Should().OnlyContain(t => t.OwnerId == Owner && t.CreatedAtUtc == Now.UtcDateTime);
        (await TagNamesOfAsync(database, person.Id)).Should().Equal("Chess", "Rock climbing");
    }

    [Fact]
    public async Task UpdateAsync_creates_a_tag_named_twice_in_one_request_only_once()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var person = await CreateService(dbContext).CreateAsync(new CreatePersonRequest { FirstName = "Ada" });

        await CreateService(dbContext).UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "Ada",
            NewTagNames = ["Chess", "chess", " CHESS "],
        });

        (await ReadTagsAsync(database)).Should().ContainSingle().Which.Name.Should().Be("Chess");
    }

    [Fact]
    public async Task UpdateAsync_does_not_duplicate_a_tag_given_by_id_and_by_name()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var tagId = await AddTagAsync(dbContext, "Chess");
        var person = await CreateService(dbContext).CreateAsync(new CreatePersonRequest { FirstName = "Ada" });

        await CreateService(dbContext).UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "Ada",
            TagIds = [tagId],
            NewTagNames = ["chess"],
        });

        (await ReadTagsAsync(database)).Should().ContainSingle();
        (await TagNamesOfAsync(database, person.Id)).Should().Equal("Chess");
    }

    [Fact]
    public async Task UpdateAsync_replaces_the_tag_set()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var oldTag = await AddTagAsync(dbContext, "Old");
        var keptTag = await AddTagAsync(dbContext, "Kept");
        var person = await CreateService(dbContext).CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            TagIds = [oldTag, keptTag],
        });

        await CreateService(dbContext).UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "Ada",
            TagIds = [keptTag],
            NewTagNames = ["Fresh"],
        });

        (await TagNamesOfAsync(database, person.Id)).Should().Equal("Fresh", "Kept");
    }

    [Fact]
    public async Task Removing_a_tag_from_a_person_keeps_the_tag()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var tagId = await AddTagAsync(dbContext, "Chess");
        var person = await CreateService(dbContext).CreateAsync(new CreatePersonRequest { FirstName = "Ada", TagIds = [tagId] });

        await CreateService(dbContext).UpdateAsync(person.Id, new UpdatePersonRequest { FirstName = "Ada" });

        (await TagNamesOfAsync(database, person.Id)).Should().BeEmpty();
        (await ReadTagsAsync(database)).Should().ContainSingle().Which.Id.Should().Be(tagId);
    }

    [Fact]
    public async Task A_rejected_update_creates_no_tags()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var person = await CreateService(dbContext).CreateAsync(new CreatePersonRequest { FirstName = "Ada" });

        var act = () => CreateService(dbContext).UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "",
            NewTagNames = ["Climbing"],
        });

        await act.Should().ThrowAsync<PersonValidationException>();
        (await ReadTagsAsync(database)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_foreign_tag_id_creates_no_new_tags()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var person = await CreateService(dbContext).CreateAsync(new CreatePersonRequest { FirstName = "Ada" });

        var act = () => CreateService(dbContext).UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "Ada",
            TagIds = [Guid.NewGuid()],
            NewTagNames = ["Climbing"],
        });

        await act.Should().ThrowAsync<ForeignEntityNotOwnedException>();
        (await ReadTagsAsync(database)).Should().BeEmpty();

        var create = () => CreateService(dbContext).CreateAsync(new CreatePersonRequest
        {
            FirstName = "Grace",
            TagIds = [Guid.NewGuid()],
            NewTagNames = ["Climbing"],
        });
        await create.Should().ThrowAsync<ForeignEntityNotOwnedException>();
        (await ReadTagsAsync(database)).Should().BeEmpty();
        (await dbContext.People.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_for_a_missing_person_creates_no_tags()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        var updated = await CreateService(dbContext).UpdateAsync(
            Guid.NewGuid(), new UpdatePersonRequest { FirstName = "Ada", NewTagNames = ["Climbing"] });

        updated.Should().BeFalse();
        (await ReadTagsAsync(database)).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_creates_new_tags_with_the_person_and_reuses_matching_ones()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var existing = await AddTagAsync(dbContext, "Work");

        var person = await CreateService(dbContext).CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            NewTagNames = ["work", "Mentor"],
        });

        (await ReadTagsAsync(database)).Select(t => t.Name).Should().BeEquivalentTo("Work", "Mentor");
        (await ReadTagsAsync(database)).Should().Contain(t => t.Id == existing);
        (await TagNamesOfAsync(database, person.Id)).Should().Equal("Mentor", "Work");
    }

    [Fact]
    public async Task GetAsync_orders_tags_by_name()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var person = await CreateService(dbContext).CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            NewTagNames = ["Zen", "Alpha", "Mid"],
        });

        var read = await CreateService(dbContext).GetAsync(person.Id);

        read!.Tags.Select(t => t.Name).Should().Equal("Alpha", "Mid", "Zen");
    }

    [Fact]
    public async Task UpdateAsync_leaves_the_tag_table_alone_when_the_request_names_no_tags()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        await AddTagAsync(dbContext, "Chess");
        var person = await CreateService(dbContext).CreateAsync(new CreatePersonRequest { FirstName = "Ada" });

        await CreateService(dbContext).UpdateAsync(person.Id, new UpdatePersonRequest { FirstName = "Ada", NewTagNames = ["  ", ""] });

        (await ReadTagsAsync(database)).Should().ContainSingle().Which.Name.Should().Be("Chess");
    }

    private static DbContextOptions<RelioDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static RelioDbContext CreateDbContext(DbContextOptions<RelioDbContext> database) =>
        new(database, new FakeTimeProvider(Now), FieldProtector);

    private static PeopleService CreateService(RelioDbContext dbContext) =>
        new(dbContext, new FakeCurrentUser(Owner), new FakeTimeProvider(Now));

    private static async Task<Guid> AddTagAsync(RelioDbContext dbContext, string name)
    {
        var tag = new Tag { OwnerId = Owner, Name = name };
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return tag.Id;
    }

    private static async Task<List<Tag>> ReadTagsAsync(DbContextOptions<RelioDbContext> database)
    {
        await using var fresh = CreateDbContext(database);
        return await fresh.Tags.AsNoTracking().ToListAsync();
    }

    private static async Task<List<string>> TagNamesOfAsync(DbContextOptions<RelioDbContext> database, Guid personId)
    {
        await using var fresh = CreateDbContext(database);
        var person = await fresh.People.AsNoTracking().Include(p => p.Tags).SingleAsync(p => p.Id == personId);
        return person.Tags.Select(t => t.Name).OrderBy(name => name, StringComparer.Ordinal).ToList();
    }
}
