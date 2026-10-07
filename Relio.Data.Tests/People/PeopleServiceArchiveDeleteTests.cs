using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// Issue #26 through <see cref="PeopleService"/>: archiving and restoring are reversible and touch
/// nothing but the person, and deleting is permanent and takes everything that belongs to the person
/// with it - the contact methods and the tag links, but never the tags themselves, another person's
/// links to them, or the relationship type - in one save, even though the InMemory provider enforces
/// no foreign keys and would otherwise leave orphans behind.
/// </summary>
public class PeopleServiceArchiveDeleteTests
{
    private const string Owner = "owner-1";

    private static readonly DateTimeOffset Now = new(2026, 10, 6, 11, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task DeleteAsync_removes_the_person_their_contact_methods_and_tag_links_but_keeps_the_tags()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);

        var deleted = await CreateService(dbContext).DeleteAsync(seeded.AdaId);

        deleted.Should().BeTrue();
        await using var fresh = CreateDbContext(database);
        (await fresh.People.AsNoTracking().AnyAsync(p => p.Id == seeded.AdaId)).Should().BeFalse();
        (await fresh.ContactMethods.AsNoTracking().Where(c => c.PersonId == seeded.AdaId).CountAsync())
            .Should().Be(0, "no contact method is left orphaned");
        (await TagLinksAsync(fresh)).Select(link => link.PersonId).Should().NotContain(seeded.AdaId, "the links go with the person");
        (await fresh.Tags.AsNoTracking().Select(t => t.Name).ToListAsync())
            .Should().BeEquivalentTo("Chess", "Climbing");
    }

    [Fact]
    public async Task DeleteAsync_keeps_other_peoples_links_and_the_relationship_type()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);

        await CreateService(dbContext).DeleteAsync(seeded.AdaId);

        await using var fresh = CreateDbContext(database);
        var grace = await fresh.People.AsNoTracking()
            .Include(p => p.Tags)
            .Include(p => p.ContactMethods)
            .SingleAsync(p => p.Id == seeded.GraceId);
        grace.Tags.Select(t => t.Name).Should().Equal("Chess");
        grace.ContactMethods.Should().ContainSingle().Which.Value.Should().Be("grace@example.com");
        (await fresh.RelationshipTypes.AsNoTracking().SingleAsync(t => t.Id == seeded.TypeId)).Name.Should().Be("Friend");
    }

    [Fact]
    public async Task DeleteAsync_for_a_nonexistent_person_returns_false_and_changes_nothing()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);

        var deleted = await CreateService(dbContext).DeleteAsync(Guid.NewGuid());

        deleted.Should().BeFalse();
        await using var fresh = CreateDbContext(database);
        (await fresh.People.AsNoTracking().CountAsync()).Should().Be(2);
        (await fresh.ContactMethods.AsNoTracking().CountAsync()).Should().Be(3);
        (await TagLinksAsync(fresh)).Should().HaveCount(3);
        (await fresh.People.AsNoTracking().AnyAsync(p => p.Id == seeded.AdaId)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_deletes_an_archived_person()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);
        var service = CreateService(dbContext);
        await service.ArchiveAsync(seeded.AdaId);

        var deleted = await service.DeleteAsync(seeded.AdaId);

        deleted.Should().BeTrue();
        await using var fresh = CreateDbContext(database);
        (await fresh.People.AsNoTracking().AnyAsync(p => p.Id == seeded.AdaId)).Should().BeFalse();
        (await fresh.ContactMethods.AsNoTracking().CountAsync(c => c.PersonId == seeded.AdaId)).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_twice_returns_false_the_second_time()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var seeded = await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        (await service.DeleteAsync(seeded.AdaId)).Should().BeTrue();
        (await service.DeleteAsync(seeded.AdaId)).Should().BeFalse();
    }

    [Fact]
    public async Task Reads_through_the_same_context_see_a_deleted_person_gone()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var seeded = await SeedAsync(dbContext);
        var service = CreateService(dbContext);
        (await service.GetAsync(seeded.AdaId)).Should().NotBeNull("the read happens first, as a circuit's would");

        await service.DeleteAsync(seeded.AdaId);

        (await service.GetAsync(seeded.AdaId)).Should().BeNull();
        (await service.ListAsync(includeArchived: true)).Select(p => p.FirstName).Should().Equal("Grace");
        var page = await service.ListPageAsync(new PeopleListQuery { IncludeArchived = true });
        page.People.Items.Select(p => p.FirstName).Should().Equal("Grace");
        page.ActiveCount.Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_leaves_nothing_tracked()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var seeded = await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        await service.DeleteAsync(seeded.AdaId);
        dbContext.ChangeTracker.Entries().Should().BeEmpty("a successful delete clears the tracker");

        await service.DeleteAsync(seeded.AdaId);
        dbContext.ChangeTracker.Entries().Should().BeEmpty("a delete that finds nobody clears the tracker too");
    }

    [Fact]
    public async Task ArchiveAsync_stamps_ArchivedAtUtc_from_the_time_provider()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);

        (await CreateService(dbContext).ArchiveAsync(seeded.AdaId)).Should().BeTrue();

        var stored = await ReadAsync(database, seeded.AdaId);
        stored.IsArchived.Should().BeTrue();
        stored.ArchivedAtUtc.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task ArchiveAsync_of_an_archived_person_returns_true_and_keeps_the_original_time()
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(Now);
        await using var dbContext = CreateDbContext(database, clock);
        var seeded = await SeedAsync(dbContext);
        var service = CreateService(dbContext, clock);
        await service.ArchiveAsync(seeded.AdaId);

        clock.Advance(TimeSpan.FromDays(3));
        (await service.ArchiveAsync(seeded.AdaId)).Should().BeTrue();

        (await ReadAsync(database, seeded.AdaId)).ArchivedAtUtc.Should().Be(Now.UtcDateTime, "archiving again keeps the first time");
    }

    [Fact]
    public async Task Archive_then_restore_keeps_every_detail_contact_method_and_tag()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);
        var service = CreateService(dbContext);

        await service.ArchiveAsync(seeded.AdaId);
        await service.RestoreAsync(seeded.AdaId);

        var restored = await service.GetAsync(seeded.AdaId);
        restored!.IsArchived.Should().BeFalse();
        restored.ArchivedAtUtc.Should().BeNull();
        restored.FirstName.Should().Be("Ada");
        restored.LastName.Should().Be("Lovelace");
        restored.RelationshipType!.Name.Should().Be("Friend");
        restored.Tags.Select(t => t.Name).Should().Equal("Chess", "Climbing");
        restored.ContactMethods.Select(c => c.Value).Should().Equal("ada@example.com", "+44 7700 900123");
    }

    [Fact]
    public async Task Archiving_leaves_the_contact_methods_and_tag_links_in_place()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);

        await CreateService(dbContext).ArchiveAsync(seeded.AdaId);

        await using var fresh = CreateDbContext(database);
        (await fresh.ContactMethods.AsNoTracking().CountAsync(c => c.PersonId == seeded.AdaId)).Should().Be(2);
        (await TagLinksAsync(fresh)).Count(link => link.PersonId == seeded.AdaId).Should().Be(2);
    }

    [Fact]
    public async Task RestoreAsync_clears_ArchivedAtUtc_and_lists_the_person_again()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var seeded = await SeedAsync(dbContext);
        var service = CreateService(dbContext);
        await service.ArchiveAsync(seeded.AdaId);
        (await service.ListAsync()).Select(p => p.FirstName).Should().Equal("Grace");

        (await service.RestoreAsync(seeded.AdaId)).Should().BeTrue();

        (await service.ListAsync()).Select(p => p.FirstName).Should().Equal("Ada", "Grace");
        (await ReadAsync(database, seeded.AdaId)).ArchivedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task RestoreAsync_of_an_active_person_returns_true_and_changes_nothing()
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(Now);
        await using var dbContext = CreateDbContext(database, clock);
        var seeded = await SeedAsync(dbContext);
        var before = await ReadAsync(database, seeded.AdaId);

        clock.Advance(TimeSpan.FromDays(1));
        (await CreateService(dbContext, clock).RestoreAsync(seeded.AdaId)).Should().BeTrue();

        var after = await ReadAsync(database, seeded.AdaId);
        after.IsArchived.Should().BeFalse();
        after.UpdatedAtUtc.Should().Be(before.UpdatedAtUtc, "nothing was written");
    }

    [Fact]
    public async Task Archive_restore_and_the_other_mutations_report_a_missing_person_as_false()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = CreateService(dbContext);

        (await service.ArchiveAsync(Guid.NewGuid())).Should().BeFalse();
        (await service.RestoreAsync(Guid.NewGuid())).Should().BeFalse();
        (await service.DeleteAsync(Guid.NewGuid())).Should().BeFalse();
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("restore")]
    [InlineData("delete")]
    public async Task Archive_restore_and_delete_without_an_authenticated_user_throw(string operation)
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = new PeopleService(dbContext, new FakeCurrentUser(null), new FakeTimeProvider(Now));

        Task Act() => operation switch
        {
            "archive" => service.ArchiveAsync(Guid.NewGuid()),
            "restore" => service.RestoreAsync(Guid.NewGuid()),
            _ => service.DeleteAsync(Guid.NewGuid()),
        };

        await FluentActions.Awaiting(Act).Should().ThrowAsync<UnauthenticatedUserException>();
    }

    private static DbContextOptions<RelioDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static RelioDbContext CreateDbContext(DbContextOptions<RelioDbContext> database, TimeProvider? timeProvider = null) =>
        new(database, timeProvider ?? new FakeTimeProvider(Now));

    private static PeopleService CreateService(RelioDbContext dbContext, TimeProvider? timeProvider = null) =>
        new(dbContext, new FakeCurrentUser(Owner), timeProvider ?? new FakeTimeProvider(Now));

    /// <summary>Reads a person back through a brand new context, so nothing can come from memory.</summary>
    private static async Task<Person> ReadAsync(DbContextOptions<RelioDbContext> database, Guid personId)
    {
        await using var fresh = CreateDbContext(database);
        return await fresh.People.AsNoTracking().SingleAsync(p => p.Id == personId);
    }

    /// <summary>Every row of the <c>PersonTags</c> join, read directly so an orphaned link cannot hide.</summary>
    private static async Task<List<(Guid PersonId, Guid TagId)>> TagLinksAsync(RelioDbContext dbContext)
    {
        var links = await dbContext.Set<Dictionary<string, object>>("PersonTag").AsNoTracking().ToListAsync();
        return links.Select(link => ((Guid)link["PeopleId"], (Guid)link["TagsId"])).ToList();
    }

    private sealed record Seeded(Guid AdaId, Guid GraceId, Guid TypeId);

    /// <summary>
    /// Ada (a Friend) with tags Chess and Climbing and two contact methods, and Grace with the Chess
    /// tag and one contact method: three tag links, three contact methods, two tags in all.
    /// </summary>
    private static async Task<Seeded> SeedAsync(RelioDbContext dbContext)
    {
        var type = new RelationshipType { OwnerId = Owner, Name = "Friend" };
        var chess = new Tag { OwnerId = Owner, Name = "Chess" };
        var climbing = new Tag { OwnerId = Owner, Name = "Climbing" };
        var ada = new Person
        {
            OwnerId = Owner,
            FirstName = "Ada",
            LastName = "Lovelace",
            RelationshipTypeId = type.Id,
            Tags = { chess, climbing },
        };
        var grace = new Person { OwnerId = Owner, FirstName = "Grace", Tags = { chess } };
        dbContext.RelationshipTypes.Add(type);
        dbContext.Tags.AddRange(chess, climbing);
        dbContext.People.AddRange(ada, grace);
        dbContext.ContactMethods.AddRange(
            Contact(ada.Id, ContactMethodKind.Email, "ada@example.com", 0),
            Contact(ada.Id, ContactMethodKind.Phone, "+44 7700 900123", 1),
            Contact(grace.Id, ContactMethodKind.Email, "grace@example.com", 0));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return new Seeded(ada.Id, grace.Id, type.Id);
    }

    private static ContactMethod Contact(Guid personId, ContactMethodKind kind, string value, int sortOrder) =>
        new()
        {
            OwnerId = Owner,
            PersonId = personId,
            Kind = kind,
            Value = value,
            NormalizedValue = ContactMethodRules.ToNormalizedValue(kind, value),
            SortOrder = sortOrder,
        };
}
