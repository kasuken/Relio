using Microsoft.EntityFrameworkCore;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// Proves the acceptance criterion of issue #10: user A cannot read, list, update, archive,
/// restore or delete user B's data, and cannot attach user B's tag to user A's person, through
/// <see cref="Relio.Data.People.PeopleService"/>.
/// </summary>
/// <remarks>
/// Uses the EF Core InMemory provider rather than SQL Server, for fast feedback on
/// service-level ownership logic without an external dependency; it does not prove SQL
/// Server-specific behaviour (constraints, indexes, query translation). See
/// <c>Relio.Data.IntegrationTests.People.PeopleServiceSqlServerOwnershipTests</c> for the same
/// scenario re-proven against a real SQL Server database - run it locally with
/// <c>ConnectionStrings__Relio=... dotnet test</c> (see the "Tests" section of AGENTS.md).
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
    public async Task ListPageAsync_only_lists_and_counts_the_current_users_people()
    {
        await using var dbContext = CreateDbContext();
        await CreatePersonAsync(dbContext, UserA, "Alice");
        var annId = await CreatePersonAsync(dbContext, UserA, "Ann");
        await CreatePersonAsync(dbContext, UserB, "Bob");
        var beaId = await CreatePersonAsync(dbContext, UserB, "Bea");
        await CreateService(dbContext, UserA).ArchiveAsync(annId);
        await CreateService(dbContext, UserB).ArchiveAsync(beaId);

        var forUserA = await CreateService(dbContext, UserA).ListPageAsync(new PeopleListQuery { IncludeArchived = true });

        forUserA.People.Items.Select(p => p.DisplayName).Should().Equal("Alice", "Ann");
        forUserA.People.TotalCount.Should().Be(2);
        forUserA.ActiveCount.Should().Be(1);
        forUserA.ArchivedCount.Should().Be(1, "the counts never include another user's people");

        var forUserB = await CreateService(dbContext, UserB).ListPageAsync(new PeopleListQuery());
        forUserB.People.Items.Select(p => p.DisplayName).Should().Equal("Bob");
        forUserB.ActiveCount.Should().Be(1);
        forUserB.ArchivedCount.Should().Be(1);
    }

    [Fact]
    public async Task ListPageAsync_for_a_user_with_no_people_says_so_even_when_others_have_some()
    {
        await using var dbContext = CreateDbContext();
        await CreatePersonAsync(dbContext, UserA, "Alice");

        var result = await CreateService(dbContext, UserB).ListPageAsync(new PeopleListQuery { IncludeArchived = true });

        result.HasAnyone.Should().BeFalse();
        result.People.Items.Should().BeEmpty();
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
        var updated = await serviceForUserB.UpdateAsync(personId, new UpdatePersonRequest { FirstName = "Eve" });

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
    public async Task DeleteAsync_for_another_users_person_returns_false_and_deletes_nothing()
    {
        await using var dbContext = CreateDbContext();
        var tag = new Tag { OwnerId = UserA, Name = "chess" };
        var person = new Person { OwnerId = UserA, FirstName = "Alice", Tags = { tag } };
        dbContext.Tags.Add(tag);
        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var personId = person.Id;
        await CreateContactMethodAsync(dbContext, UserA, personId, "alice@example.com");

        var deleted = await CreateService(dbContext, UserB).DeleteAsync(personId);

        deleted.Should().BeFalse();
        var untouched = await CreateService(dbContext, UserA).GetAsync(personId);
        untouched!.FirstName.Should().Be("Alice");
        untouched.ContactMethods.Should().ContainSingle();
        untouched.Tags.Should().ContainSingle();
        (await dbContext.ContactMethods.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_reports_another_users_person_and_a_nonexistent_one_the_same_way()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice");
        var serviceForUserB = CreateService(dbContext, UserB);

        var foreign = await serviceForUserB.DeleteAsync(personId);
        var missing = await serviceForUserB.DeleteAsync(Guid.NewGuid());

        // The primary entity is "no result" in both cases: nothing says which one it was.
        foreign.Should().BeFalse();
        missing.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_cannot_attach_another_users_tag()
    {
        await using var dbContext = CreateDbContext();
        var tagIdOwnedByUserB = await CreateTagAsync(dbContext, UserB, "family");

        var serviceForUserA = CreateService(dbContext, UserA);
        var act = () => serviceForUserA.CreateAsync(
            new CreatePersonRequest { FirstName = "Alice", TagIds = [tagIdOwnedByUserB] });

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
            personId, new UpdatePersonRequest { FirstName = "Alice", TagIds = [tagIdOwnedByUserB] });

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
        var person = await serviceForUserA.CreateAsync(new CreatePersonRequest { FirstName = "Alice", TagIds = [tagId] });

        person.Tags.Should().ContainSingle(t => t.Id == tagId);
    }

    [Fact]
    public async Task CreateAsync_cannot_assign_another_users_relationship_type()
    {
        await using var dbContext = CreateDbContext();
        var typeIdOwnedByUserB = await CreateRelationshipTypeAsync(dbContext, UserB, "Friend");

        var serviceForUserA = CreateService(dbContext, UserA);
        var act = () => serviceForUserA.CreateAsync(
            new CreatePersonRequest { FirstName = "Alice", RelationshipTypeId = typeIdOwnedByUserB });

        (await act.Should().ThrowAsync<ForeignEntityNotOwnedException>())
            .WithMessage("*relationship types*");

        (await dbContext.People.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_reports_a_nonexistent_and_a_foreign_relationship_type_with_the_same_message()
    {
        await using var dbContext = CreateDbContext();
        var typeIdOwnedByUserB = await CreateRelationshipTypeAsync(dbContext, UserB, "Friend");
        var serviceForUserA = CreateService(dbContext, UserA);

        var foreign = await FluentActions
            .Awaiting(() => serviceForUserA.CreateAsync(
                new CreatePersonRequest { FirstName = "Alice", RelationshipTypeId = typeIdOwnedByUserB }))
            .Should().ThrowAsync<ForeignEntityNotOwnedException>();
        var missing = await FluentActions
            .Awaiting(() => serviceForUserA.CreateAsync(
                new CreatePersonRequest { FirstName = "Alice", RelationshipTypeId = Guid.NewGuid() }))
            .Should().ThrowAsync<ForeignEntityNotOwnedException>();

        // A request must never be able to tell "doesn't exist" from "belongs to someone else".
        foreign.Which.Message.Should().Be(missing.Which.Message);
    }

    [Fact]
    public async Task UpdateAsync_cannot_assign_another_users_relationship_type()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice");
        var typeIdOwnedByUserB = await CreateRelationshipTypeAsync(dbContext, UserB, "Friend");

        var serviceForUserA = CreateService(dbContext, UserA);
        var act = () => serviceForUserA.UpdateAsync(
            personId, new UpdatePersonRequest { FirstName = "Alice", RelationshipTypeId = typeIdOwnedByUserB });

        await act.Should().ThrowAsync<ForeignEntityNotOwnedException>();

        var unchanged = await dbContext.People.AsNoTracking().SingleAsync(p => p.Id == personId);
        unchanged.RelationshipTypeId.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_can_assign_the_current_users_own_relationship_type()
    {
        await using var dbContext = CreateDbContext();
        var typeId = await CreateRelationshipTypeAsync(dbContext, UserA, "Friend");

        var serviceForUserA = CreateService(dbContext, UserA);
        var person = await serviceForUserA.CreateAsync(
            new CreatePersonRequest { FirstName = "Alice", RelationshipTypeId = typeId });

        person.RelationshipTypeId.Should().Be(typeId);
        var stored = await serviceForUserA.GetAsync(person.Id);
        stored!.RelationshipType!.Name.Should().Be("Friend");
    }

    [Fact]
    public async Task UpdateAsync_for_another_users_person_returns_false_even_with_an_invalid_relationship_type()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice");

        var serviceForUserB = CreateService(dbContext, UserB);
        var updated = await serviceForUserB.UpdateAsync(
            personId, new UpdatePersonRequest { FirstName = "Eve", RelationshipTypeId = Guid.NewGuid() });

        // The primary entity is reported as "no result" before any foreign id is looked at, so the
        // answer for another user's person is the same whatever else the request contains.
        updated.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_cannot_attach_another_users_tag_even_alongside_a_new_tag_name()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice");
        var tagIdOwnedByUserB = await CreateTagAsync(dbContext, UserB, "family");

        var act = () => CreateService(dbContext, UserA).UpdateAsync(
            personId,
            new UpdatePersonRequest { FirstName = "Alice", TagIds = [tagIdOwnedByUserB], NewTagNames = ["Climbing"] });

        (await act.Should().ThrowAsync<ForeignEntityNotOwnedException>()).Which.EntityName.Should().Be("tags");

        // Nothing was half done: no "Climbing" tag appeared, and nothing was attached.
        (await dbContext.Tags.AsNoTracking().Where(t => t.OwnerId == UserA).CountAsync()).Should().Be(0);
        (await dbContext.People.AsNoTracking().Include(p => p.Tags).SingleAsync(p => p.Id == personId)).Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_cannot_edit_another_users_contact_method()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice");
        var otherPersonId = await CreatePersonAsync(dbContext, UserB, "Bob");
        var contactMethodIdOwnedByUserB = await CreateContactMethodAsync(dbContext, UserB, otherPersonId, "bob@example.com");

        var act = () => CreateService(dbContext, UserA).UpdateAsync(
            personId,
            new UpdatePersonRequest
            {
                FirstName = "Alice",
                ContactMethods = [new ContactMethodInput(contactMethodIdOwnedByUserB, ContactMethodKind.Email, null, "alice@example.com")],
            });

        (await act.Should().ThrowAsync<ForeignEntityNotOwnedException>()).Which.EntityName.Should().Be("contact methods");

        var untouched = await dbContext.ContactMethods.AsNoTracking().SingleAsync();
        untouched.Id.Should().Be(contactMethodIdOwnedByUserB);
        untouched.OwnerId.Should().Be(UserB);
        untouched.PersonId.Should().Be(otherPersonId);
        untouched.Value.Should().Be("bob@example.com");
    }

    [Fact]
    public async Task UpdateAsync_cannot_move_a_contact_method_to_another_person_of_the_same_user()
    {
        await using var dbContext = CreateDbContext();
        var firstPersonId = await CreatePersonAsync(dbContext, UserA, "Alice");
        var secondPersonId = await CreatePersonAsync(dbContext, UserA, "Ann");
        var contactMethodId = await CreateContactMethodAsync(dbContext, UserA, firstPersonId, "alice@example.com");

        var act = () => CreateService(dbContext, UserA).UpdateAsync(
            secondPersonId,
            new UpdatePersonRequest
            {
                FirstName = "Ann",
                ContactMethods = [new ContactMethodInput(contactMethodId, ContactMethodKind.Email, null, "alice@example.com")],
            });

        await act.Should().ThrowAsync<ForeignEntityNotOwnedException>();

        (await dbContext.ContactMethods.AsNoTracking().SingleAsync()).PersonId.Should().Be(firstPersonId);
    }

    [Fact]
    public async Task UpdateAsync_reports_a_nonexistent_and_a_foreign_contact_method_id_with_the_same_message()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice");
        var otherPersonId = await CreatePersonAsync(dbContext, UserB, "Bob");
        var foreignId = await CreateContactMethodAsync(dbContext, UserB, otherPersonId, "bob@example.com");
        var service = CreateService(dbContext, UserA);

        Task<bool> UpdateWith(Guid contactMethodId) => service.UpdateAsync(
            personId,
            new UpdatePersonRequest
            {
                FirstName = "Alice",
                ContactMethods = [new ContactMethodInput(contactMethodId, ContactMethodKind.Email, null, "a@example.com")],
            });

        var foreign = await FluentActions.Awaiting(() => UpdateWith(foreignId)).Should().ThrowAsync<ForeignEntityNotOwnedException>();
        var missing = await FluentActions.Awaiting(() => UpdateWith(Guid.NewGuid())).Should().ThrowAsync<ForeignEntityNotOwnedException>();

        foreign.Which.Message.Should().Be(missing.Which.Message);
    }

    [Fact]
    public async Task UpdateAsync_for_another_users_person_returns_false_even_with_foreign_contact_method_ids_and_new_tag_names()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice");
        var contactMethodId = await CreateContactMethodAsync(dbContext, UserA, personId, "alice@example.com");

        var updated = await CreateService(dbContext, UserB).UpdateAsync(
            personId,
            new UpdatePersonRequest
            {
                FirstName = "Eve",
                TagIds = [Guid.NewGuid()],
                NewTagNames = ["Climbing"],
                ContactMethods = [new ContactMethodInput(contactMethodId, ContactMethodKind.Email, null, "eve@example.com")],
            });

        // "No result" for the person comes before any foreign id is looked at, and creates nothing.
        updated.Should().BeFalse();
        (await dbContext.Tags.AsNoTracking().CountAsync()).Should().Be(0);
        (await dbContext.ContactMethods.AsNoTracking().SingleAsync()).Value.Should().Be("alice@example.com");
        (await dbContext.People.AsNoTracking().SingleAsync()).FirstName.Should().Be("Alice");
    }

    [Fact]
    public async Task CreateAsync_rejects_any_contact_method_id()
    {
        await using var dbContext = CreateDbContext();
        var otherPersonId = await CreatePersonAsync(dbContext, UserB, "Bob");
        var foreignId = await CreateContactMethodAsync(dbContext, UserB, otherPersonId, "bob@example.com");
        var service = CreateService(dbContext, UserA);

        foreach (var id in new[] { foreignId, Guid.NewGuid() })
        {
            var act = () => service.CreateAsync(new CreatePersonRequest
            {
                FirstName = "Alice",
                ContactMethods = [new ContactMethodInput(id, ContactMethodKind.Email, null, "alice@example.com")],
            });

            (await act.Should().ThrowAsync<ForeignEntityNotOwnedException>()).Which.EntityName.Should().Be("contact methods");
        }

        (await dbContext.People.AsNoTracking().CountAsync(p => p.OwnerId == UserA)).Should().Be(0);
        (await dbContext.ContactMethods.AsNoTracking().SingleAsync()).OwnerId.Should().Be(UserB);
    }

    [Fact]
    public async Task CreateAsync_with_a_new_tag_name_creates_it_for_the_current_user_only()
    {
        await using var dbContext = CreateDbContext();
        await CreateTagAsync(dbContext, UserB, "Climbing");

        var person = await CreateService(dbContext, UserA).CreateAsync(
            new CreatePersonRequest { FirstName = "Alice", NewTagNames = ["climbing"] });

        // User B's "Climbing" is not user A's: a new tag is made for A, and B's is untouched.
        var tags = await dbContext.Tags.AsNoTracking().ToListAsync();
        tags.Should().HaveCount(2);
        tags.Single(t => t.OwnerId == UserA).Name.Should().Be("climbing");
        tags.Single(t => t.OwnerId == UserB).Name.Should().Be("Climbing");
        person.Tags.Should().ContainSingle().Which.OwnerId.Should().Be(UserA);
    }

    [Fact]
    public async Task GetAsync_for_another_users_person_never_returns_their_contact_methods()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice");
        await CreateContactMethodAsync(dbContext, UserA, personId, "alice@example.com");

        (await CreateService(dbContext, UserB).GetAsync(personId)).Should().BeNull();
        (await CreateService(dbContext, UserA).GetAsync(personId))!.ContactMethods.Should().ContainSingle();
    }

    [Fact]
    public async Task FindPossibleDuplicatesAsync_never_matches_another_users_people()
    {
        await using var dbContext = CreateDbContext();
        var activeId = await CreatePersonAsync(dbContext, UserB, "John");
        var archived = new Person { OwnerId = UserB, FirstName = "John", IsArchived = true };
        dbContext.People.Add(archived);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var archivedId = archived.Id;
        foreach (var id in new[] { activeId, archivedId })
        {
            await CreateContactMethodAsync(dbContext, UserB, id, "john@example.com");
            await CreatePhoneAsync(dbContext, UserB, id, "+44 7700 900123");
        }

        var query = new PossibleDuplicateQuery
        {
            FirstName = "John",
            ContactMethods =
            [
                new ContactMethodInput(null, ContactMethodKind.Email, null, "john@example.com"),
                new ContactMethodInput(null, ContactMethodKind.Phone, null, "07700 900123"),
            ],
        };
        var ownId = await CreatePersonAsync(dbContext, UserA, "John");

        var forUserA = await CreateService(dbContext, UserA).FindPossibleDuplicatesAsync(query);
        var forUserB = await CreateService(dbContext, UserB).FindPossibleDuplicatesAsync(query);

        forUserA.Should().ContainSingle().Which.Id.Should().Be(ownId);
        forUserB.Select(match => match.Id).Should().BeEquivalentTo([activeId, archivedId]);
    }

    [Fact]
    public async Task FindPossibleDuplicatesAsync_excluding_another_users_person_id_changes_nothing()
    {
        await using var dbContext = CreateDbContext();
        var mine = await CreatePersonAsync(dbContext, UserA, "John");
        var theirs = await CreatePersonAsync(dbContext, UserB, "John");

        var result = await CreateService(dbContext, UserA).FindPossibleDuplicatesAsync(
            new PossibleDuplicateQuery { FirstName = "John", ExcludePersonId = theirs });

        result.Should().ContainSingle().Which.Id.Should().Be(mine);
    }

    [Fact]
    public async Task Operations_without_an_authenticated_user_throw()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, userId: null);

        var act = () => service.ListAsync();

        await act.Should().ThrowAsync<UnauthenticatedUserException>();
    }

    [Fact]
    public async Task ListPageAsync_without_an_authenticated_user_throws()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, userId: null);

        var act = () => service.ListPageAsync(new PeopleListQuery());

        await act.Should().ThrowAsync<UnauthenticatedUserException>();
    }

    private static RelioDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RelioDbContext(options, TimeProvider.System, FieldProtector);
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

    private static async Task<Guid> CreateRelationshipTypeAsync(RelioDbContext dbContext, string ownerId, string name)
    {
        var type = new RelationshipType { OwnerId = ownerId, Name = name };
        dbContext.RelationshipTypes.Add(type);
        await dbContext.SaveChangesAsync();
        return type.Id;
    }

    private static async Task<Guid> CreateContactMethodAsync(
        RelioDbContext dbContext, string ownerId, Guid personId, string value)
    {
        var contactMethod = new ContactMethod
        {
            OwnerId = ownerId,
            PersonId = personId,
            Kind = ContactMethodKind.Email,
            Value = value,
            NormalizedValue = ContactMethodRules.ToNormalizedValue(ContactMethodKind.Email, value),
        };
        dbContext.ContactMethods.Add(contactMethod);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return contactMethod.Id;
    }

    private static async Task CreatePhoneAsync(RelioDbContext dbContext, string ownerId, Guid personId, string value)
    {
        dbContext.ContactMethods.Add(new ContactMethod
        {
            OwnerId = ownerId,
            PersonId = personId,
            Kind = ContactMethodKind.Phone,
            Value = value,
            NormalizedValue = ContactMethodRules.ToNormalizedValue(ContactMethodKind.Phone, value),
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private static async Task<Guid> CreateTagAsync(RelioDbContext dbContext, string ownerId, string name)
    {
        var tag = new Tag { OwnerId = ownerId, Name = name };
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync();
        return tag.Id;
    }
}
