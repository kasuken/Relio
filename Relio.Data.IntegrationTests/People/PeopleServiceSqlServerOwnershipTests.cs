using Microsoft.EntityFrameworkCore;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.People;

/// <summary>
/// Re-proves, against a real SQL Server database, the cross-user isolation scenarios from
/// <c>Relio.Data.Tests.People.PeopleServiceOwnershipTests</c> (which uses the EF Core InMemory
/// provider): user B cannot read, list, update, archive or restore user A's person, and cannot
/// attach their own tag to user A's person, through <see cref="Relio.Data.People.PeopleService"/>.
/// This also exercises the LINQ query translation (Include, OrderBy/ThenBy, Contains) that
/// InMemory does not actually prove.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PeopleServiceSqlServerOwnershipTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task GetAsync_for_another_users_person_returns_null()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, ownerA, "Alice");

        var serviceForB = TestDataFactory.CreateService(dbContext, ownerB);
        var result = await serviceForB.GetAsync(personId);

        result.Should().BeNull();
    }

    [SqlServerFact]
    public async Task GetAsync_for_a_nonexistent_person_also_returns_null()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();

        var serviceForA = TestDataFactory.CreateService(dbContext, ownerA);
        var result = await serviceForA.GetAsync(Guid.NewGuid());

        // Not-found and not-owned must be indistinguishable to the caller.
        result.Should().BeNull();
    }

    [SqlServerFact]
    public async Task ListAsync_only_returns_the_current_users_people()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        await TestDataFactory.CreatePersonAsync(dbContext, ownerA, "Alice");
        await TestDataFactory.CreatePersonAsync(dbContext, ownerB, "Bob");

        var serviceForA = TestDataFactory.CreateService(dbContext, ownerA);
        var people = await serviceForA.ListAsync();

        people.Should().ContainSingle().Which.DisplayName.Should().Be("Alice");
    }

    [SqlServerFact]
    public async Task ListAsync_excludes_archived_people_by_default()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, ownerA, "Alice");

        var serviceForA = TestDataFactory.CreateService(dbContext, ownerA);
        await serviceForA.ArchiveAsync(personId);

        (await serviceForA.ListAsync()).Should().BeEmpty();
        (await serviceForA.ListAsync(includeArchived: true)).Should().ContainSingle();
    }

    [SqlServerFact]
    public async Task UpdateAsync_for_another_users_person_returns_false_and_does_not_modify_it()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, ownerA, "Alice");

        var serviceForB = TestDataFactory.CreateService(dbContext, ownerB);
        var updated = await serviceForB.UpdateAsync(personId, new UpdatePersonRequest { FirstName = "Eve" });

        updated.Should().BeFalse();
        var stillOwnedByA = await dbContext.People.AsNoTracking().SingleAsync(p => p.Id == personId);
        stillOwnedByA.FirstName.Should().Be("Alice");
    }

    [SqlServerFact]
    public async Task ArchiveAsync_for_another_users_person_returns_false_and_does_not_archive_it()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, ownerA, "Alice");

        var serviceForB = TestDataFactory.CreateService(dbContext, ownerB);
        var archived = await serviceForB.ArchiveAsync(personId);

        archived.Should().BeFalse();
        var stillActive = await dbContext.People.AsNoTracking().SingleAsync(p => p.Id == personId);
        stillActive.IsArchived.Should().BeFalse();
    }

    [SqlServerFact]
    public async Task RestoreAsync_for_another_users_person_returns_false_and_does_not_restore_it()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, ownerA, "Alice");
        await TestDataFactory.CreateService(dbContext, ownerA).ArchiveAsync(personId);

        var serviceForB = TestDataFactory.CreateService(dbContext, ownerB);
        var restored = await serviceForB.RestoreAsync(personId);

        restored.Should().BeFalse();
        var stillArchived = await dbContext.People.AsNoTracking().SingleAsync(p => p.Id == personId);
        stillArchived.IsArchived.Should().BeTrue();
    }

    [SqlServerFact]
    public async Task CreateAsync_cannot_attach_another_users_tag()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        var tagIdOwnedByB = await TestDataFactory.CreateTagAsync(dbContext, ownerB, "family");

        var serviceForA = TestDataFactory.CreateService(dbContext, ownerA);
        var act = () => serviceForA.CreateAsync(new CreatePersonRequest { FirstName = "Alice", TagIds = [tagIdOwnedByB] });

        (await act.Should().ThrowAsync<ForeignEntityNotOwnedException>())
            .WithMessage("*tags*");

        (await dbContext.People.Where(p => p.OwnerId == ownerA).CountAsync()).Should().Be(0);
    }

    [SqlServerFact]
    public async Task UpdateAsync_cannot_attach_another_users_tag()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, ownerA, "Alice");
        var tagIdOwnedByB = await TestDataFactory.CreateTagAsync(dbContext, ownerB, "family");

        var serviceForA = TestDataFactory.CreateService(dbContext, ownerA);
        var act = () => serviceForA.UpdateAsync(
            personId, new UpdatePersonRequest { FirstName = "Alice", TagIds = [tagIdOwnedByB] });

        await act.Should().ThrowAsync<ForeignEntityNotOwnedException>();

        var unchanged = await dbContext.People.AsNoTracking()
            .Include(p => p.Tags)
            .SingleAsync(p => p.Id == personId);
        unchanged.Tags.Should().BeEmpty();
    }

    [SqlServerFact]
    public async Task CreateAsync_can_attach_the_current_users_own_tag()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var tagId = await TestDataFactory.CreateTagAsync(dbContext, ownerA, "family");

        var serviceForA = TestDataFactory.CreateService(dbContext, ownerA);
        var person = await serviceForA.CreateAsync(new CreatePersonRequest { FirstName = "Alice", TagIds = [tagId] });

        person.Tags.Should().ContainSingle(t => t.Id == tagId);
    }

    [SqlServerFact]
    public async Task CreateAsync_cannot_assign_another_users_relationship_type()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        var typeIdOwnedByB = await TestDataFactory.CreateRelationshipTypeAsync(dbContext, ownerB, "Friend");

        var serviceForA = TestDataFactory.CreateService(dbContext, ownerA);
        var act = () => serviceForA.CreateAsync(
            new CreatePersonRequest { FirstName = "Alice", RelationshipTypeId = typeIdOwnedByB });

        (await act.Should().ThrowAsync<ForeignEntityNotOwnedException>())
            .WithMessage("*relationship types*");

        (await dbContext.People.Where(p => p.OwnerId == ownerA).CountAsync()).Should().Be(0);
    }

    [SqlServerFact]
    public async Task UpdateAsync_cannot_assign_another_users_relationship_type()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, ownerA, "Alice");
        var typeIdOwnedByB = await TestDataFactory.CreateRelationshipTypeAsync(dbContext, ownerB, "Friend");

        var serviceForA = TestDataFactory.CreateService(dbContext, ownerA);
        var act = () => serviceForA.UpdateAsync(
            personId, new UpdatePersonRequest { FirstName = "Alice", RelationshipTypeId = typeIdOwnedByB });

        await act.Should().ThrowAsync<ForeignEntityNotOwnedException>();

        var unchanged = await dbContext.People.AsNoTracking().SingleAsync(p => p.Id == personId);
        unchanged.RelationshipTypeId.Should().BeNull();
    }

    [SqlServerFact]
    public async Task CreateAsync_round_trips_every_optional_detail()
    {
        var ownerA = TestDataFactory.NewOwnerId();
        Guid personId;
        Guid typeId;
        await using (var writeContext = fixture.CreateDbContext())
        {
            typeId = await TestDataFactory.CreateRelationshipTypeAsync(writeContext, ownerA, "Friend");
            var created = await TestDataFactory.CreateService(writeContext, ownerA).CreateAsync(new CreatePersonRequest
            {
                FirstName = "Ada",
                LastName = "Lovelace",
                Nickname = "Countess",
                RelationshipTypeId = typeId,
                BirthdayDay = 29,
                BirthdayMonth = 2,
                HowWeMet = "At a talk about engines.",
                Details = "Writes long letters.\nLikes music - and éè accents.",
            });
            personId = created.Id;
        }

        // A brand new context: whatever comes back was really stored.
        await using var readContext = fixture.CreateDbContext();
        var stored = await TestDataFactory.CreateService(readContext, ownerA).GetAsync(personId);

        stored.Should().NotBeNull();
        stored!.FirstName.Should().Be("Ada");
        stored.LastName.Should().Be("Lovelace");
        stored.Nickname.Should().Be("Countess");
        stored.RelationshipTypeId.Should().Be(typeId);
        stored.RelationshipType!.Name.Should().Be("Friend");
        stored.BirthdayYear.Should().BeNull();
        stored.Birthday.Should().Be(Birthday.Create(2, 29));
        stored.HowWeMet.Should().Be("At a talk about engines.");
        stored.Details.Should().Be("Writes long letters.\nLikes music - and éè accents.");
    }

    [SqlServerFact]
    public async Task ListAsync_includes_the_relationship_type()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var typeId = await TestDataFactory.CreateRelationshipTypeAsync(dbContext, ownerA, "Colleague");
        var service = TestDataFactory.CreateService(dbContext, ownerA);
        await service.CreateAsync(new CreatePersonRequest { FirstName = "Elena", RelationshipTypeId = typeId });

        var people = await service.ListAsync();

        people.Should().ContainSingle().Which.RelationshipType!.Name.Should().Be("Colleague");
    }

    [SqlServerFact]
    public async Task RelationshipTypeService_lists_only_the_current_users_types_in_order()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        await TestDataFactory.CreateRelationshipTypeAsync(dbContext, ownerA, "Friend", sortOrder: 1);
        await TestDataFactory.CreateRelationshipTypeAsync(dbContext, ownerA, "Family", sortOrder: 0);
        await TestDataFactory.CreateRelationshipTypeAsync(dbContext, ownerB, "Rival", sortOrder: 0);

        var types = await TestDataFactory.CreateRelationshipTypeService(dbContext, ownerA).ListAsync();

        types.Select(t => t.Name).Should().Equal("Family", "Friend");
    }
}
