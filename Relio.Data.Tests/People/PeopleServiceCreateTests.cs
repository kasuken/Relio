using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// Issue #22 through <see cref="PeopleService"/>: what a created or updated person looks like in
/// the database, what is rejected (and that a rejection saves nothing), the user's own "today" for
/// the future-birthday rule, and that reads and writes stay correct when one
/// <see cref="RelioDbContext"/> lives as long as a Blazor circuit.
/// </summary>
public class PeopleServiceCreateTests
{
    private const string Owner = "owner-1";

    // 11:30 UTC on 6 October 2026: still the 6th in the UTC and Rome, already the 7th on Kiritimati (UTC+14).
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 11, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_with_only_a_first_name_stores_a_person_owned_by_the_current_user()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        var created = await CreateService(dbContext).CreateAsync(new CreatePersonRequest { FirstName = "Ada" });

        var stored = await ReadAsync(database, created.Id);
        stored.OwnerId.Should().Be(Owner);
        stored.FirstName.Should().Be("Ada");
        stored.LastName.Should().BeNull();
        stored.Nickname.Should().BeNull();
        stored.RelationshipTypeId.Should().BeNull();
        stored.BirthdayDay.Should().BeNull();
        stored.BirthdayMonth.Should().BeNull();
        stored.BirthdayYear.Should().BeNull();
        stored.Birthday.Should().BeNull();
        stored.HowWeMet.Should().BeNull();
        stored.Details.Should().BeNull();
        stored.IsArchived.Should().BeFalse();
        stored.CreatedAtUtc.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task CreateAsync_stores_every_optional_detail()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var typeId = await AddRelationshipTypeAsync(dbContext, Owner, "Friend");

        var created = await CreateService(dbContext).CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Nickname = "Countess",
            RelationshipTypeId = typeId,
            BirthdayDay = 10,
            BirthdayMonth = 12,
            BirthdayYear = 1815,
            HowWeMet = "At a talk about engines.",
            Details = "Writes long letters.\nLikes music.",
        });

        var stored = await ReadAsync(database, created.Id);
        stored.LastName.Should().Be("Lovelace");
        stored.Nickname.Should().Be("Countess");
        stored.RelationshipTypeId.Should().Be(typeId);
        stored.RelationshipType!.Name.Should().Be("Friend");
        stored.Birthday.Should().Be(Birthday.Create(12, 10, 1815));
        stored.HowWeMet.Should().Be("At a talk about engines.");
        stored.Details.Should().Be("Writes long letters.\nLikes music.");
    }

    [Fact]
    public async Task CreateAsync_trims_text_and_stores_blank_optional_fields_as_null()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        var created = await CreateService(dbContext).CreateAsync(new CreatePersonRequest
        {
            FirstName = "  Ada  ",
            LastName = "   ",
            Nickname = "",
            HowWeMet = "\n",
            Details = "  Likes music.  ",
        });

        var stored = await ReadAsync(database, created.Id);
        stored.FirstName.Should().Be("Ada");
        stored.LastName.Should().BeNull();
        stored.Nickname.Should().BeNull();
        stored.HowWeMet.Should().BeNull();
        stored.Details.Should().Be("Likes music.");
    }

    [Fact]
    public async Task CreateAsync_rejects_invalid_input_with_codes_and_saves_nothing()
    {
        await using var dbContext = CreateDbContext(NewDatabase());

        var act = () => CreateService(dbContext).CreateAsync(new CreatePersonRequest
        {
            FirstName = "   ",
            Details = new string('x', Person.DetailsMaxLength + 1),
            BirthdayDay = 12,
        });

        var exception = (await act.Should().ThrowAsync<PersonValidationException>()).Which;
        exception.Errors.Should().BeEquivalentTo(
            [
                PersonValidationError.FirstNameRequired,
                PersonValidationError.DetailsTooLong,
                PersonValidationError.BirthdayIncomplete,
            ]);
        exception.Message.Should().NotContain("xxx", "the message lists codes only, never submitted values");
        (await dbContext.People.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_accepts_a_birthday_without_a_year()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        var created = await CreateService(dbContext).CreateAsync(
            new CreatePersonRequest { FirstName = "Liam", BirthdayDay = 29, BirthdayMonth = 2 });

        var stored = await ReadAsync(database, created.Id);
        stored.BirthdayYear.Should().BeNull();
        stored.Birthday.Should().Be(Birthday.Create(2, 29));
    }

    [Fact]
    public async Task CreateAsync_allows_tomorrow_in_UTC_for_a_Kiritimati_user_but_not_for_a_UTC_user()
    {
        // 7 October 2026 is "today" on Kiritimati at the fixed instant, and tomorrow in UTC.
        var request = new CreatePersonRequest { FirstName = "Ada", BirthdayDay = 7, BirthdayMonth = 10, BirthdayYear = 2026 };

        await using var kiritimati = CreateDbContext(NewDatabase());
        await AddProfileAsync(kiritimati, "Pacific/Kiritimati");
        var created = await CreateService(kiritimati).CreateAsync(request);
        created.BirthdayYear.Should().Be(2026);

        await using var utc = CreateDbContext(NewDatabase());
        await AddProfileAsync(utc, "UTC");
        var act = () => CreateService(utc).CreateAsync(request);
        (await act.Should().ThrowAsync<PersonValidationException>()).Which.Errors
            .Should().Equal(PersonValidationError.BirthdayInTheFuture);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_date_that_is_tomorrow_for_a_Pago_Pago_user_even_though_it_is_today_in_UTC()
    {
        // 05:00 UTC on 6 October is still the evening of the 5th in Pago Pago (UTC-11).
        var earlier = new FakeTimeProvider(new DateTimeOffset(2026, 10, 6, 5, 0, 0, TimeSpan.Zero));
        await using var dbContext = CreateDbContext(NewDatabase(), earlier);
        await AddProfileAsync(dbContext, "Pacific/Pago_Pago");

        var act = () => CreateService(dbContext, earlier).CreateAsync(
            new CreatePersonRequest { FirstName = "Ada", BirthdayDay = 6, BirthdayMonth = 10, BirthdayYear = 2026 });

        (await act.Should().ThrowAsync<PersonValidationException>()).Which.Errors
            .Should().Equal(PersonValidationError.BirthdayInTheFuture);
    }

    [Fact]
    public async Task CreateAsync_without_a_profile_uses_UTC_for_today()
    {
        await using var dbContext = CreateDbContext(NewDatabase());

        var created = await CreateService(dbContext).CreateAsync(
            new CreatePersonRequest { FirstName = "Ada", BirthdayDay = 6, BirthdayMonth = 10, BirthdayYear = 2026 });

        created.BirthdayYear.Should().Be(2026);
    }

    [Fact]
    public async Task UpdateAsync_replaces_every_profile_field_and_clears_the_ones_left_out()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var typeId = await AddRelationshipTypeAsync(dbContext, Owner, "Friend");
        var service = CreateService(dbContext);
        var created = await service.CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Nickname = "Countess",
            RelationshipTypeId = typeId,
            BirthdayDay = 10,
            BirthdayMonth = 12,
            BirthdayYear = 1815,
            HowWeMet = "At a talk.",
            Details = "Writes long letters.",
        });

        var updated = await service.UpdateAsync(created.Id, new UpdatePersonRequest { FirstName = "Augusta" });

        updated.Should().BeTrue();
        var stored = await ReadAsync(database, created.Id);
        stored.FirstName.Should().Be("Augusta");
        stored.LastName.Should().BeNull();
        stored.Nickname.Should().BeNull();
        stored.RelationshipTypeId.Should().BeNull();
        stored.BirthdayDay.Should().BeNull();
        stored.BirthdayMonth.Should().BeNull();
        stored.BirthdayYear.Should().BeNull();
        stored.HowWeMet.Should().BeNull();
        stored.Details.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_rejects_invalid_input_and_leaves_the_person_untouched()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var service = CreateService(dbContext);
        var created = await service.CreateAsync(new CreatePersonRequest { FirstName = "Ada" });

        var act = () => service.UpdateAsync(created.Id, new UpdatePersonRequest { FirstName = "" });

        await act.Should().ThrowAsync<PersonValidationException>();
        (await ReadAsync(database, created.Id)).FirstName.Should().Be("Ada");
    }

    [Fact]
    public async Task Reads_see_changes_made_through_another_context()
    {
        // The scenario a long-lived circuit creates: the context that read a person earlier must not
        // answer the next read from memory.
        var database = NewDatabase();
        await using var circuitContext = CreateDbContext(database);
        var circuitService = CreateService(circuitContext);
        var created = await circuitService.CreateAsync(new CreatePersonRequest { FirstName = "Ada" });
        (await circuitService.GetAsync(created.Id))!.FirstName.Should().Be("Ada");

        await using (var otherContext = CreateDbContext(database))
        {
            await CreateService(otherContext).UpdateAsync(created.Id, new UpdatePersonRequest { FirstName = "Augusta" });
        }

        (await circuitService.GetAsync(created.Id))!.FirstName.Should().Be("Augusta");
        (await circuitService.ListAsync()).Should().ContainSingle().Which.FirstName.Should().Be("Augusta");
    }

    [Fact]
    public async Task Mutations_leave_nothing_tracked()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = CreateService(dbContext);

        var created = await service.CreateAsync(new CreatePersonRequest { FirstName = "Ada" });
        dbContext.ChangeTracker.Entries().Should().BeEmpty("create clears the tracker");

        await service.UpdateAsync(created.Id, new UpdatePersonRequest { FirstName = "Ada", LastName = "L" });
        dbContext.ChangeTracker.Entries().Should().BeEmpty("update clears the tracker");

        await service.ArchiveAsync(created.Id);
        dbContext.ChangeTracker.Entries().Should().BeEmpty("archive clears the tracker");

        await service.RestoreAsync(created.Id);
        dbContext.ChangeTracker.Entries().Should().BeEmpty("restore clears the tracker");

        await service.DeleteAsync(created.Id);
        dbContext.ChangeTracker.Entries().Should().BeEmpty("delete clears the tracker");

        await service.GetAsync(created.Id);
        await service.ListAsync();
        dbContext.ChangeTracker.Entries().Should().BeEmpty("reads are untracked");
    }

    [Fact]
    public async Task A_failed_create_leaves_nothing_tracked_to_be_saved_by_the_next_call()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = CreateService(dbContext);

        var act = () => service.CreateAsync(
            new CreatePersonRequest { FirstName = "Ada", TagIds = [Guid.NewGuid()] });
        await act.Should().ThrowAsync<ForeignEntityNotOwnedException>();

        dbContext.ChangeTracker.Entries().Should().BeEmpty();
        await service.CreateAsync(new CreatePersonRequest { FirstName = "Grace" });
        (await dbContext.People.AsNoTracking().Select(p => p.FirstName).ToListAsync()).Should().Equal("Grace");
    }

    [Fact]
    public async Task ListAsync_includes_the_relationship_type()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var typeId = await AddRelationshipTypeAsync(dbContext, Owner, "Colleague");
        var service = CreateService(dbContext);
        await service.CreateAsync(new CreatePersonRequest { FirstName = "Elena", RelationshipTypeId = typeId });
        await service.CreateAsync(new CreatePersonRequest { FirstName = "Ada" });

        var people = await service.ListAsync();

        people.Select(p => p.FirstName).Should().Equal("Ada", "Elena");
        people[0].RelationshipType.Should().BeNull();
        people[1].RelationshipType!.Name.Should().Be("Colleague");
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
        return await fresh.People.AsNoTracking()
            .Include(p => p.RelationshipType)
            .SingleAsync(p => p.Id == personId);
    }

    private static async Task<Guid> AddRelationshipTypeAsync(RelioDbContext dbContext, string ownerId, string name)
    {
        var type = new RelationshipType { OwnerId = ownerId, Name = name };
        dbContext.RelationshipTypes.Add(type);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return type.Id;
    }

    private static async Task AddProfileAsync(RelioDbContext dbContext, string timeZoneId)
    {
        dbContext.UserProfiles.Add(new UserProfile { OwnerId = Owner, TimeZoneId = timeZoneId });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }
}
