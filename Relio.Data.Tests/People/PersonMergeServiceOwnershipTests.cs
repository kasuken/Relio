using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.People;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// Cross-user isolation for <see cref="PersonMergeService"/> (issue #28): user A can never merge, or
/// even learn about, user B's people, and a refused merge changes nothing of B's. InMemory; the same
/// scenario runs on SQL Server in <c>PersonMergeSqlServerTests</c>.
/// </summary>
public class PersonMergeServiceOwnershipTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    private static readonly DateTimeOffset Now = new(2026, 10, 6, 11, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task MergeAsync_with_another_users_duplicate_returns_NotFound_and_changes_nothing()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var a = await SeedAsync(dbContext, UserA, "Alice");
        var b = await SeedAsync(dbContext, UserB, "Alice");

        var outcome = await CreateService(dbContext, UserA).MergeAsync(new MergePeopleRequest { PrimaryId = a.PersonId, DuplicateId = b.PersonId });

        outcome.Should().Be(MergeOutcome.NotFound);
        await using var fresh = CreateDbContext(database);
        (await fresh.People.AsNoTracking().CountAsync()).Should().Be(2);
        (await fresh.ContactMethods.AsNoTracking().CountAsync(c => c.PersonId == b.PersonId)).Should().Be(1, "B's contact method is still B's");
        (await fresh.ContactMethods.AsNoTracking().CountAsync(c => c.PersonId == a.PersonId)).Should().Be(1);
        (await TagLinksAsync(fresh)).Should().Contain(link => link.PersonId == b.PersonId && link.TagId == b.TagId);
        (await fresh.People.AsNoTracking().SingleAsync(p => p.Id == a.PersonId)).Details.Should().BeNull();
    }

    [Fact]
    public async Task MergeAsync_with_another_users_primary_returns_NotFound_and_changes_nothing()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var a = await SeedAsync(dbContext, UserA, "Alice");
        var b = await SeedAsync(dbContext, UserB, "Alice");

        var outcome = await CreateService(dbContext, UserA).MergeAsync(new MergePeopleRequest { PrimaryId = b.PersonId, DuplicateId = a.PersonId });

        outcome.Should().Be(MergeOutcome.NotFound);
        await using var fresh = CreateDbContext(database);
        (await fresh.People.AsNoTracking().CountAsync()).Should().Be(2);
        (await fresh.ContactMethods.AsNoTracking().CountAsync(c => c.PersonId == a.PersonId)).Should().Be(1, "A's person was not merged into B's");
        (await fresh.ContactMethods.AsNoTracking().CountAsync(c => c.PersonId == b.PersonId)).Should().Be(1);
    }

    [Fact]
    public async Task A_nonexistent_and_a_foreign_id_give_the_same_outcome()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var a = await SeedAsync(dbContext, UserA, "Alice");
        var b = await SeedAsync(dbContext, UserB, "Alice");
        var service = CreateService(dbContext, UserA);

        var foreign = await service.MergeAsync(new MergePeopleRequest { PrimaryId = a.PersonId, DuplicateId = b.PersonId });
        var nonexistent = await service.MergeAsync(new MergePeopleRequest { PrimaryId = a.PersonId, DuplicateId = Guid.NewGuid() });

        foreign.Should().Be(nonexistent);
    }

    [Fact]
    public async Task ListCandidatesAsync_for_another_users_person_returns_null()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var b = await SeedAsync(dbContext, UserB, "Alice");

        (await CreateService(dbContext, UserA).ListCandidatesAsync(b.PersonId)).Should().BeNull();
    }

    [Fact]
    public async Task ListCandidatesAsync_never_lists_or_suggests_another_users_people()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var a = await SeedAsync(dbContext, UserA, "Alice");
        var b = await SeedAsync(dbContext, UserB, "Alice");

        var candidates = await CreateService(dbContext, UserA).ListCandidatesAsync(a.PersonId);

        candidates!.Others.Should().BeEmpty();
        candidates.Suggestions.Should().BeEmpty("B's Alice has the same name and the same email, but she is B's");
        candidates.Others.Concat(candidates.Suggestions.Select(s => new PersonSummary(s.Id, s.FirstName, s.LastName, s.IsArchived)))
            .Should().NotContain(p => p.Id == b.PersonId);
    }

    private static DbContextOptions<RelioDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static RelioDbContext CreateDbContext(DbContextOptions<RelioDbContext> database) =>
        new(database, new FakeTimeProvider(Now));

    private static PersonMergeService CreateService(RelioDbContext dbContext, string userId) =>
        new(dbContext, new FakeCurrentUser(userId), new FakeTimeProvider(Now));

    private static async Task<List<(Guid PersonId, Guid TagId)>> TagLinksAsync(RelioDbContext dbContext)
    {
        var links = await dbContext.Set<Dictionary<string, object>>("PersonTag").AsNoTracking().ToListAsync();
        return links.Select(link => ((Guid)link["PeopleId"], (Guid)link["TagsId"])).ToList();
    }

    private sealed record Seeded(Guid PersonId, Guid TagId);

    /// <summary>One person for <paramref name="owner"/> with the same name and email as every other owner's, a tag and a contact method.</summary>
    private static async Task<Seeded> SeedAsync(RelioDbContext dbContext, string owner, string firstName)
    {
        var tag = new Tag { OwnerId = owner, Name = "Chess" };
        var person = new Person { OwnerId = owner, FirstName = firstName, Tags = { tag } };
        dbContext.Tags.Add(tag);
        dbContext.People.Add(person);
        dbContext.ContactMethods.Add(new ContactMethod
        {
            OwnerId = owner,
            PersonId = person.Id,
            Kind = ContactMethodKind.Email,
            Value = "alice@example.com",
            NormalizedValue = ContactMethodRules.ToNormalizedValue(ContactMethodKind.Email, "alice@example.com"),
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return new Seeded(person.Id, tag.Id);
    }
}
