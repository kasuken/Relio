using Microsoft.EntityFrameworkCore;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// Proves the acceptance criterion of issue #10: user A cannot read, list, update, archive or
/// restore user B's data, and cannot attach user B's tag to user A's person, through
/// <see cref="Relio.Data.People.PeopleService"/>.
/// </summary>
/// <remarks>
/// Uses the EF Core InMemory provider rather than SQL Server. Local Docker is not available in
/// this environment, so these tests exercise service-level ownership logic only; they do not
/// prove SQL Server-specific behaviour (constraints, indexes, query translation). The ef-core
/// skill prefers SQL Server integration tests for that - issue #13 adds a SQL Server-backed
/// integration test project that will cover this same scenario end to end.
/// </remarks>
public class PeopleServiceOwnershipTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    [Fact]
    public async Task GetAsync_for_another_users_person_returns_null()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice");

        var serviceForUserB = CreateService(dbContext, UserB);
        var result = await serviceForUserB.GetAsync(personId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_for_a_nonexistent_person_also_returns_null()
    {
        await using var dbContext = CreateDbContext();

        var serviceForUserA = CreateService(dbContext, UserA);
        var result = await serviceForUserA.GetAsync(Guid.NewGuid());

        // Not-found and not-owned must be indistinguishable to the caller.
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_only_returns_the_current_users_people()
    {
        await using var dbContext = CreateDbContext();
        await CreatePersonAsync(dbContext, UserA, "Alice");
        await CreatePersonAsync(dbContext, UserB, "Bob");

        var serviceForUserA = CreateService(dbContext, UserA);
        var people = await serviceForUserA.ListAsync();

        people.Should().ContainSingle().Which.DisplayName.Should().Be("Alice");
    }

    [Fact]
    public async Task ListAsync_excludes_archived_people_by_default()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice");

        var serviceForUserA = CreateService(dbContext, UserA);
        await serviceForUserA.ArchiveAsync(personId);

        (await serviceForUserA.ListAsync()).Should().BeEmpty();
        (await serviceForUserA.ListAsync(includeArchived: true)).Should().ContainSingle();
    }

    [Fact]
    public async Task UpdateAsync_for_another_users_person_returns_false_and_does_not_modify_it()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice");

        var serviceForUserB = CreateService(dbContext, UserB);
        var updated = await serviceForUserB.UpdateAsync(personId, new UpdatePersonRequest("Eve", null, null));

        updated.Should().BeFalse();
        var stillOwnedByUserA = await dbContext.People.AsNoTracking().SingleAsync(p => p.Id == personId);
        stillOwnedByUserA.FirstName.Should().Be("Alice");
    }

    [Fact]
    public async Task ArchiveAsync_for_another_users_person_returns_false_and_does_not_archive_it()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice");

        var serviceForUserB = CreateService(dbContext, UserB);
        var archived = await serviceForUserB.ArchiveAsync(personId);

        archived.Should().BeFalse();
        var stillActive = await dbContext.People.AsNoTracking().SingleAsync(p => p.Id == personId);
        stillActive.IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreAsync_for_another_users_person_returns_false_and_does_not_restore_it()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice");
        await CreateService(dbContext, UserA).ArchiveAsync(personId);

        var serviceForUserB = CreateService(dbContext, UserB);
        var restored = await serviceForUserB.RestoreAsync(personId);

        restored.Should().BeFalse();
        var stillArchived = await dbContext.People.AsNoTracking().SingleAsync(p => p.Id == personId);
        stillArchived.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_cannot_attach_another_users_tag()
    {
        await using var dbContext = CreateDbContext();
        var tagIdOwnedByUserB = await CreateTagAsync(dbContext, UserB, "family");

        var serviceForUserA = CreateService(dbContext, UserA);
        var act = () => serviceForUserA.CreateAsync(
            new CreatePersonRequest("Alice", null, null, [tagIdOwnedByUserB]));

        (await act.Should().ThrowAsync<ForeignEntityNotOwnedException>())
            .WithMessage("*tags*");

        (await dbContext.People.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_cannot_attach_another_users_tag()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice");
        var tagIdOwnedByUserB = await CreateTagAsync(dbContext, UserB, "family");

        var serviceForUserA = CreateService(dbContext, UserA);
        var act = () => serviceForUserA.UpdateAsync(
            personId, new UpdatePersonRequest("Alice", null, null, [tagIdOwnedByUserB]));

        await act.Should().ThrowAsync<ForeignEntityNotOwnedException>();

        var unchanged = await dbContext.People.AsNoTracking()
            .Include(p => p.Tags)
            .SingleAsync(p => p.Id == personId);
        unchanged.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_can_attach_the_current_users_own_tag()
    {
        await using var dbContext = CreateDbContext();
        var tagId = await CreateTagAsync(dbContext, UserA, "family");

        var serviceForUserA = CreateService(dbContext, UserA);
        var person = await serviceForUserA.CreateAsync(new CreatePersonRequest("Alice", null, null, [tagId]));

        person.Tags.Should().ContainSingle(t => t.Id == tagId);
    }

    [Fact]
    public async Task Operations_without_an_authenticated_user_throw()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, userId: null);

        var act = () => service.ListAsync();

        await act.Should().ThrowAsync<UnauthenticatedUserException>();
    }

    private static RelioDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RelioDbContext(options, TimeProvider.System);
    }

    private static Relio.Data.People.PeopleService CreateService(RelioDbContext dbContext, string? userId) =>
        new(dbContext, new FakeCurrentUser(userId), TimeProvider.System);

    private static async Task<Guid> CreatePersonAsync(RelioDbContext dbContext, string ownerId, string firstName)
    {
        var person = new Person { OwnerId = ownerId, FirstName = firstName };
        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();
        return person.Id;
    }

    private static async Task<Guid> CreateTagAsync(RelioDbContext dbContext, string ownerId, string name)
    {
        var tag = new Tag { OwnerId = ownerId, Name = name };
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync();
        return tag.Id;
    }
}
