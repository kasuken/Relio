using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// Issue #24 through <see cref="PeopleService"/>: contact methods are created with a person and
/// replaced as a whole list on update (diffed by id), with their comparison key written by
/// <see cref="ContactMethodRules"/>, and nothing is saved when any row is invalid or any id is not
/// this person's.
/// </summary>
public class PeopleServiceContactMethodTests
{
    private const string Owner = "owner-1";

    private static readonly DateTimeOffset Now = new(2026, 10, 6, 11, 30, 0, TimeSpan.Zero);

    private static ContactMethodInput Email(string value, string? label = null, Guid? id = null) =>
        new(id, ContactMethodKind.Email, label, value);

    private static ContactMethodInput Phone(string value, string? label = null, Guid? id = null) =>
        new(id, ContactMethodKind.Phone, label, value);

    [Fact]
    public async Task CreateAsync_stores_contact_methods_in_order_with_normalized_values()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        var created = await CreateService(dbContext).CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            ContactMethods =
            [
                Email("  Ada@Example.COM ", " Work "),
                Phone("+44 (7700) 900-123", "Mobile"),
                new ContactMethodInput(null, ContactMethodKind.Address, "  ", "12 Example Square\r\nLondon"),
            ],
        });

        var stored = await ReadContactMethodsAsync(database, created.Id);
        stored.Select(c => c.SortOrder).Should().Equal(0, 1, 2);
        stored.Select(c => c.Kind).Should().Equal(ContactMethodKind.Email, ContactMethodKind.Phone, ContactMethodKind.Address);
        stored.Select(c => c.Value).Should().Equal("Ada@Example.COM", "+44 (7700) 900-123", "12 Example Square\nLondon");
        stored.Select(c => c.NormalizedValue).Should().Equal("ada@example.com", "+447700900123", "12 example square london");
        stored.Select(c => c.Label).Should().Equal("Work", "Mobile", null);
        stored.Should().OnlyContain(c => c.OwnerId == Owner && c.PersonId == created.Id);
        stored.Should().OnlyContain(c => c.CreatedAtUtc == Now.UtcDateTime);
    }

    [Fact]
    public async Task UpdateAsync_adds_updates_and_removes_contact_methods_by_id()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var service = CreateService(dbContext);
        var person = await service.CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            ContactMethods = [Email("ada@example.com", "Work"), Phone("+44 7700 900123"), Email("old@example.com")],
        });
        var stored = (await service.GetAsync(person.Id))!.ContactMethods.OrderBy(c => c.SortOrder).ToList();

        var updated = await service.UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "Ada",
            ContactMethods =
            [
                Email("ada.l@example.com", "Personal", stored[0].Id),   // edited
                Phone("+39 333 123 4567", null),                         // new
                // stored[1] and stored[2] are not in the request: removed
            ],
        });

        updated.Should().BeTrue();
        var after = await ReadContactMethodsAsync(database, person.Id);
        after.Select(c => c.Value).Should().Equal("ada.l@example.com", "+39 333 123 4567");
        after.Select(c => c.Label).Should().Equal("Personal", null);
        after.Select(c => c.NormalizedValue).Should().Equal("ada.l@example.com", "+393331234567");
        after[0].Id.Should().Be(stored[0].Id);
        after.Select(c => c.Id).Should().NotContain([stored[1].Id, stored[2].Id]);
        (await CountContactMethodsAsync(database)).Should().Be(2, "removed rows are deleted, not left behind");
    }

    [Fact]
    public async Task UpdateAsync_keeps_the_id_and_creation_time_of_an_edited_contact_method()
    {
        var database = NewDatabase();
        var time = new FakeTimeProvider(Now);
        await using var dbContext = CreateDbContext(database, time);
        var service = CreateService(dbContext, time);
        var person = await service.CreateAsync(new CreatePersonRequest { FirstName = "Ada", ContactMethods = [Email("ada@example.com")] });
        var original = (await service.GetAsync(person.Id))!.ContactMethods.Single();

        time.Advance(TimeSpan.FromHours(3));
        await service.UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "Ada",
            ContactMethods = [Email("ada@example.org", id: original.Id)],
        });

        var edited = (await ReadContactMethodsAsync(database, person.Id)).Single();
        edited.Id.Should().Be(original.Id);
        edited.CreatedAtUtc.Should().Be(Now.UtcDateTime);
        edited.UpdatedAtUtc.Should().Be(Now.UtcDateTime.AddHours(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateAsync_with_no_contact_methods_removes_them_all(bool emptyList)
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var service = CreateService(dbContext);
        var person = await service.CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            ContactMethods = [Email("ada@example.com"), Phone("+44 7700 900123")],
        });

        await service.UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "Ada",
            ContactMethods = emptyList ? [] : null,
        });

        (await CountContactMethodsAsync(database)).Should().Be(0);
        (await service.GetAsync(person.Id))!.ContactMethods.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_follows_the_request_order()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var service = CreateService(dbContext);
        var person = await service.CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            ContactMethods = [Email("one@example.com"), Email("two@example.com"), Email("three@example.com")],
        });
        var stored = (await service.GetAsync(person.Id))!.ContactMethods.OrderBy(c => c.SortOrder).ToList();

        await service.UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "Ada",
            ContactMethods =
            [
                Email("three@example.com", id: stored[2].Id),
                Email("four@example.com"),
                Email("one@example.com", id: stored[0].Id),
            ],
        });

        var after = await ReadContactMethodsAsync(database, person.Id);
        after.Select(c => c.Value).Should().Equal("three@example.com", "four@example.com", "one@example.com");
        after.Select(c => c.SortOrder).Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task UpdateAsync_rejects_invalid_contact_methods_with_their_index_and_saves_nothing()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var service = CreateService(dbContext);
        var person = await service.CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            ContactMethods = [Email("ada@example.com")],
        });
        var existing = (await service.GetAsync(person.Id))!.ContactMethods.Single();

        var act = () => service.UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "Changed",
            NewTagNames = ["Climbing"],
            ContactMethods =
            [
                Email("fine@example.com", id: existing.Id),
                Email("not an email"),
                Phone("12"),
                Phone("+44 7700 900123"),
            ],
        });

        var exception = (await act.Should().ThrowAsync<PersonValidationException>()).Which;
        exception.ContactMethodProblems.Should().Equal(
            new ContactMethodProblem(1, ContactMethodValidationError.EmailInvalid),
            new ContactMethodProblem(2, ContactMethodValidationError.PhoneDigitCount));
        exception.Message.Should().NotContain("example.com").And.NotContain("Climbing").And.NotContain("Changed");

        (await service.GetAsync(person.Id))!.FirstName.Should().Be("Ada");
        (await ReadContactMethodsAsync(database, person.Id)).Single().Value.Should().Be("ada@example.com");
        (await CountTagsAsync(database)).Should().Be(0, "a rejected update creates no tags");
    }

    [Fact]
    public async Task CreateAsync_reports_profile_errors_and_contact_method_problems_together()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        var act = () => CreateService(dbContext).CreateAsync(new CreatePersonRequest
        {
            FirstName = "",
            ContactMethods = [Phone("abc")],
        });

        var exception = (await act.Should().ThrowAsync<PersonValidationException>()).Which;
        exception.Errors.Should().Equal(PersonValidationError.FirstNameRequired);
        exception.ContactMethodProblems.Should().Equal(new ContactMethodProblem(0, ContactMethodValidationError.PhoneInvalidCharacters));
        (await dbContext.People.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_rejects_more_than_20_contact_methods()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var service = CreateService(dbContext);
        var person = await service.CreateAsync(new CreatePersonRequest { FirstName = "Ada" });

        var twentyOne = Enumerable.Range(0, 21).Select(i => Email($"p{i}@example.com")).ToList();
        var act = () => service.UpdateAsync(person.Id, new UpdatePersonRequest { FirstName = "Ada", ContactMethods = twentyOne });

        (await act.Should().ThrowAsync<PersonValidationException>()).Which.Errors
            .Should().Equal(PersonValidationError.TooManyContactMethods);

        await service.UpdateAsync(person.Id, new UpdatePersonRequest { FirstName = "Ada", ContactMethods = twentyOne.Take(20).ToList() });
        (await CountContactMethodsAsync(database)).Should().Be(20);
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_repeated_contact_method_id()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var service = CreateService(dbContext);
        var person = await service.CreateAsync(new CreatePersonRequest { FirstName = "Ada", ContactMethods = [Email("ada@example.com")] });
        var existing = (await service.GetAsync(person.Id))!.ContactMethods.Single();

        var act = () => service.UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "Ada",
            ContactMethods = [Email("a@example.com", id: existing.Id), Email("b@example.com", id: existing.Id)],
        });

        await act.Should().ThrowAsync<ArgumentException>();
        (await ReadContactMethodsAsync(database, person.Id)).Single().Value.Should().Be("ada@example.com");
    }

    [Fact]
    public async Task UpdateAsync_with_a_foreign_contact_method_id_changes_nothing_at_all()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var service = CreateService(dbContext);
        var person = await service.CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            ContactMethods = [Email("ada@example.com"), Phone("+44 7700 900123")],
        });
        var existing = (await service.GetAsync(person.Id))!.ContactMethods.OrderBy(c => c.SortOrder).ToList();

        var act = () => service.UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "Changed",
            Nickname = "New nickname",
            NewTagNames = ["Climbing"],
            ContactMethods =
            [
                Email("edited@example.com", id: existing[0].Id),
                Email("new@example.com"),
                Email("stale@example.com", id: Guid.NewGuid()),
            ],
        });

        (await act.Should().ThrowAsync<ForeignEntityNotOwnedException>()).Which.EntityName.Should().Be("contact methods");

        var person2 = await service.GetAsync(person.Id);
        person2!.FirstName.Should().Be("Ada");
        person2.Nickname.Should().BeNull();
        person2.ContactMethods.Select(c => c.Value).Should().Equal("ada@example.com", "+44 7700 900123");
        (await CountTagsAsync(database)).Should().Be(0);
        (await CountContactMethodsAsync(database)).Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_returns_contact_methods_in_sort_order()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var person = new Person { OwnerId = Owner, FirstName = "Ada" };
        person.ContactMethods.Add(NewContactMethod("c@example.com", 2));
        person.ContactMethods.Add(NewContactMethod("a@example.com", 0));
        person.ContactMethods.Add(NewContactMethod("b@example.com", 1));
        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var read = await CreateService(dbContext).GetAsync(person.Id);

        read!.ContactMethods.Select(c => c.Value).Should().Equal("a@example.com", "b@example.com", "c@example.com");
    }

    [Fact]
    public async Task GetAsync_sees_contact_method_changes_made_through_another_context()
    {
        // One context plays a circuit that stays open for hours; the other is somebody else's save.
        var database = NewDatabase();
        await using var circuit = CreateDbContext(database);
        await using var elsewhere = CreateDbContext(database);
        var person = await CreateService(circuit).CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            ContactMethods = [Email("ada@example.com")],
        });
        var seenFirst = await CreateService(circuit).GetAsync(person.Id);
        seenFirst!.ContactMethods.Should().ContainSingle();

        var stored = (await CreateService(elsewhere).GetAsync(person.Id))!.ContactMethods.Single();
        await CreateService(elsewhere).UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "Ada",
            ContactMethods = [Email("changed@example.com", id: stored.Id), Phone("+44 7700 900123")],
        });

        var seenAgain = await CreateService(circuit).GetAsync(person.Id);
        seenAgain!.ContactMethods.Select(c => c.Value).Should().Equal("changed@example.com", "+44 7700 900123");
    }

    [Fact]
    public async Task Contact_method_mutations_leave_nothing_tracked()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var service = CreateService(dbContext);

        var person = await service.CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            NewTagNames = ["Climbing"],
            ContactMethods = [Email("ada@example.com")],
        });
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        var stored = (await service.GetAsync(person.Id))!.ContactMethods.Single();
        await service.UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "Ada",
            NewTagNames = ["Chess"],
            ContactMethods = [Email("b@example.com", id: stored.Id), Email("c@example.com")],
        });
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        var invalid = () => service.UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "Ada",
            ContactMethods = [Email("stale@example.com", id: Guid.NewGuid())],
        });
        await invalid.Should().ThrowAsync<ForeignEntityNotOwnedException>();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        await service.GetAsync(person.Id);
        dbContext.ChangeTracker.Entries().Should().BeEmpty("reads are untracked");
    }

    private static ContactMethod NewContactMethod(string value, int sortOrder) => new()
    {
        OwnerId = Owner,
        Kind = ContactMethodKind.Email,
        Value = value,
        NormalizedValue = value,
        SortOrder = sortOrder,
    };

    private static DbContextOptions<RelioDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static RelioDbContext CreateDbContext(DbContextOptions<RelioDbContext> database, TimeProvider? timeProvider = null) =>
        new(database, timeProvider ?? new FakeTimeProvider(Now));

    private static PeopleService CreateService(RelioDbContext dbContext, TimeProvider? timeProvider = null) =>
        new(dbContext, new FakeCurrentUser(Owner), timeProvider ?? new FakeTimeProvider(Now));

    private static async Task<List<ContactMethod>> ReadContactMethodsAsync(DbContextOptions<RelioDbContext> database, Guid personId)
    {
        await using var fresh = CreateDbContext(database);
        return await fresh.ContactMethods.AsNoTracking()
            .Where(c => c.PersonId == personId)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
    }

    private static async Task<int> CountContactMethodsAsync(DbContextOptions<RelioDbContext> database)
    {
        await using var fresh = CreateDbContext(database);
        return await fresh.ContactMethods.CountAsync();
    }

    private static async Task<int> CountTagsAsync(DbContextOptions<RelioDbContext> database)
    {
        await using var fresh = CreateDbContext(database);
        return await fresh.Tags.CountAsync();
    }
}
