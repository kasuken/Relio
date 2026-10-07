using Microsoft.EntityFrameworkCore;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// Issue #27 through <see cref="PeopleService.FindPossibleDuplicatesAsync"/>: the matching rules
/// are proven in <c>Relio.Application.Tests</c>; this proves the data is loaded, scoped and
/// translated into candidates correctly.
/// </summary>
public class PeopleServiceDuplicateTests
{
    private const string Owner = "owner-1";

    [Fact]
    public async Task FindPossibleDuplicatesAsync_finds_John_Smith_for_Jon_Smith()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var john = await AddPersonAsync(dbContext, "John", "Smith");
        await AddPersonAsync(dbContext, "Ada", "Lovelace");

        var result = await CreateService(dbContext).FindPossibleDuplicatesAsync(new PossibleDuplicateQuery { FirstName = "Jon", LastName = "Smith" });

        result.Should().ContainSingle();
        result[0].Id.Should().Be(john);
        result[0].DisplayName.Should().Be("John Smith");
        result[0].Reasons.Should().Equal(PossibleDuplicateReason.SimilarName);
    }

    [Fact]
    public async Task FindPossibleDuplicatesAsync_includes_archived_people()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        await AddPersonAsync(dbContext, "John", "Smith", archived: true);

        var result = await CreateService(dbContext).FindPossibleDuplicatesAsync(new PossibleDuplicateQuery { FirstName = "John", LastName = "Smith" });

        result.Should().ContainSingle().Which.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task FindPossibleDuplicatesAsync_matches_a_stored_email_typed_in_another_case()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var ada = await AddPersonAsync(dbContext, "Ada", "Byron");
        await AddContactAsync(dbContext, ada, ContactMethodKind.Email, "Ada@Example.com");

        var result = await CreateService(dbContext).FindPossibleDuplicatesAsync(new PossibleDuplicateQuery
        {
            FirstName = "Grace",
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, " ADA@example.COM ")],
        });

        result.Should().ContainSingle().Which.Reasons.Should().Equal(PossibleDuplicateReason.SameEmail);
    }

    [Fact]
    public async Task FindPossibleDuplicatesAsync_matches_a_stored_phone_on_its_last_eight_digits()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var ada = await AddPersonAsync(dbContext, "Ada", "Byron");
        await AddContactAsync(dbContext, ada, ContactMethodKind.Phone, "+44 7700 900123");

        var service = CreateService(dbContext);
        var found = await service.FindPossibleDuplicatesAsync(new PossibleDuplicateQuery
        {
            FirstName = "Grace",
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Phone, null, "07700 900123")],
        });
        var different = await service.FindPossibleDuplicatesAsync(new PossibleDuplicateQuery
        {
            FirstName = "Grace",
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Phone, null, "07700 900124")],
        });

        found.Should().ContainSingle().Which.Reasons.Should().Equal(PossibleDuplicateReason.SamePhone);
        different.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPossibleDuplicatesAsync_does_not_count_an_address_equal_to_an_email()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var ada = await AddPersonAsync(dbContext, "Ada", "Byron");
        await AddContactAsync(dbContext, ada, ContactMethodKind.Address, "ada@example.com");

        var result = await CreateService(dbContext).FindPossibleDuplicatesAsync(new PossibleDuplicateQuery
        {
            FirstName = "Grace",
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "ada@example.com")],
        });

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPossibleDuplicatesAsync_excludes_the_person_being_edited()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var self = await AddPersonAsync(dbContext, "John", "Smith");
        var other = await AddPersonAsync(dbContext, "John", "Smith");

        var result = await CreateService(dbContext).FindPossibleDuplicatesAsync(
            new PossibleDuplicateQuery { FirstName = "John", LastName = "Smith", ExcludePersonId = self });

        result.Should().ContainSingle().Which.Id.Should().Be(other);
    }

    [Fact]
    public async Task FindPossibleDuplicatesAsync_returns_at_most_five()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        for (var index = 0; index < 8; index++)
        {
            await AddPersonAsync(dbContext, "John", "Smith");
        }

        var result = await CreateService(dbContext).FindPossibleDuplicatesAsync(new PossibleDuplicateQuery { FirstName = "John", LastName = "Smith" });

        result.Should().HaveCount(5);
    }

    [Fact]
    public async Task FindPossibleDuplicatesAsync_with_nothing_to_compare_returns_nothing()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        await AddPersonAsync(dbContext, "John", "Smith");
        var service = CreateService(dbContext);

        (await service.FindPossibleDuplicatesAsync(new PossibleDuplicateQuery())).Should().BeEmpty();
        (await service.FindPossibleDuplicatesAsync(new PossibleDuplicateQuery { FirstName = "  ", LastName = "Smith" })).Should().BeEmpty();
    }

    [Fact]
    public async Task FindPossibleDuplicatesAsync_leaves_nothing_tracked()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var ada = await AddPersonAsync(dbContext, "John", "Smith");
        await AddContactAsync(dbContext, ada, ContactMethodKind.Email, "john@example.com");

        await CreateService(dbContext).FindPossibleDuplicatesAsync(new PossibleDuplicateQuery
        {
            FirstName = "Jon",
            LastName = "Smith",
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "john@example.com")],
        });

        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task FindPossibleDuplicatesAsync_sees_people_added_through_another_context()
    {
        var database = NewDatabase();
        await using var circuit = CreateDbContext(database);
        var service = CreateService(circuit);
        var query = new PossibleDuplicateQuery { FirstName = "John", LastName = "Smith" };
        (await service.FindPossibleDuplicatesAsync(query)).Should().BeEmpty();

        await using (var elsewhere = CreateDbContext(database))
        {
            await AddPersonAsync(elsewhere, "John", "Smith");
        }

        (await service.FindPossibleDuplicatesAsync(query)).Should().ContainSingle();
    }

    [Fact]
    public async Task FindPossibleDuplicatesAsync_rejects_a_null_query()
    {
        await using var dbContext = CreateDbContext(NewDatabase());

        var act = () => CreateService(dbContext).FindPossibleDuplicatesAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task FindPossibleDuplicatesAsync_without_an_authenticated_user_throws()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = new PeopleService(dbContext, new FakeCurrentUser(null), TimeProvider.System);

        var act = () => service.FindPossibleDuplicatesAsync(new PossibleDuplicateQuery { FirstName = "John" });

        await act.Should().ThrowAsync<UnauthenticatedUserException>();
    }

    private static DbContextOptions<RelioDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static RelioDbContext CreateDbContext(DbContextOptions<RelioDbContext> database) =>
        new(database, TimeProvider.System, FieldProtector);

    private static PeopleService CreateService(RelioDbContext dbContext) =>
        new(dbContext, new FakeCurrentUser(Owner), TimeProvider.System);

    private static async Task<Guid> AddPersonAsync(RelioDbContext dbContext, string first, string? last, bool archived = false)
    {
        var person = new Person { OwnerId = Owner, FirstName = first, LastName = last, IsArchived = archived };
        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return person.Id;
    }

    private static async Task AddContactAsync(RelioDbContext dbContext, Guid personId, ContactMethodKind kind, string value)
    {
        dbContext.ContactMethods.Add(new ContactMethod
        {
            OwnerId = Owner,
            PersonId = personId,
            Kind = kind,
            Value = value,
            NormalizedValue = ContactMethodRules.ToNormalizedValue(kind, value),
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }
}
