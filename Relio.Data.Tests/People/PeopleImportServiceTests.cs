using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.People;
using Relio.Application.People.Import;
using Relio.Application.Security;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// Issue #29 through <see cref="PeopleImportService"/>: the preview is read-only and flags the user's
/// existing people; the import creates everyone it is given with one save and refuses (saving nothing)
/// anything malformed. InMemory provider; <c>PeopleImportSqlServerTests</c> proves the transaction.
/// </summary>
public class PeopleImportServiceTests
{
    private const string Owner = "owner-1";

    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    // ---- ImportAsync -----------------------------------------------------------------------

    [Fact]
    public async Task ImportAsync_creates_every_person_with_contact_methods_in_order_and_normalized_values()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        var created = await CreateService(dbContext).ImportAsync(
        [
            Request("Ada", "Lovelace", contacts:
            [
                new ContactMethodInput(null, ContactMethodKind.Email, "Home", "  Ada@Example.COM "),
                new ContactMethodInput(null, ContactMethodKind.Phone, null, "+44 7700 900123"),
                new ContactMethodInput(null, ContactMethodKind.Address, "Work", "1 Example Road\r\nLondon"),
            ]),
            Request("Grace", null),
        ]);

        created.Should().Be(2);
        await using var fresh = CreateDbContext(database);
        var ada = await CreatePeopleService(fresh).ListAsync();
        ada.Select(p => p.DisplayName).Should().Equal("Ada Lovelace", "Grace");

        var adaId = ada[0].Id;
        var methods = await fresh.ContactMethods.AsNoTracking().Where(c => c.PersonId == adaId).OrderBy(c => c.SortOrder).ToListAsync();
        methods.Select(c => c.Value).Should().Equal("Ada@Example.COM", "+44 7700 900123", "1 Example Road\nLondon");
        methods.Select(c => c.NormalizedValue).Should().Equal("ada@example.com", "+447700900123", "1 example road london");
        methods.Select(c => c.SortOrder).Should().Equal(0, 1, 2);
        methods.Select(c => c.Label).Should().Equal("Home", null, "Work");
        methods.Should().OnlyContain(c => c.OwnerId == Owner);
    }

    [Fact]
    public async Task ImportAsync_stores_yearless_birthdays_without_a_year()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        await CreateService(dbContext).ImportAsync(
        [
            Request("Ada", null, birthday: (4, 15, null)),
            Request("Zoe", null, birthday: (2, 29, null)),
            Request("Bea", null, birthday: (12, 10, 1985)),
        ]);

        await using var fresh = CreateDbContext(database);
        var people = await fresh.People.AsNoTracking().OrderBy(p => p.FirstName).ToListAsync();
        people.Select(p => p.Birthday).Should().Equal(
            Domain.Birthday.Create(4, 15), Domain.Birthday.Create(12, 10, 1985), Domain.Birthday.Create(2, 29));
        people[0].BirthdayYear.Should().BeNull();
    }

    [Fact]
    public async Task ImportAsync_saves_once()
    {
        var counter = new CountingSaveChangesInterceptor();
        await using var dbContext = CreateDbContext(NewDatabase(), counter);

        await CreateService(dbContext).ImportAsync(Enumerable.Range(0, 25).Select(i => Request($"Person{i}", null, contacts: [new ContactMethodInput(null, ContactMethodKind.Email, null, $"p{i}@example.com")])).ToList());

        counter.Saves.Should().Be(1);
    }

    [Fact]
    public async Task ImportAsync_rejects_invalid_people_with_their_index_and_saves_nothing()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        var act = () => CreateService(dbContext).ImportAsync(
        [
            Request("Ada", null),
            Request(" ", null),
            Request("Bea", null, contacts: [new ContactMethodInput(null, ContactMethodKind.Email, null, "not-an-email")]),
        ]);

        var thrown = (await act.Should().ThrowAsync<PeopleImportValidationException>()).Which;
        thrown.Rows.Select(row => row.Index).Should().Equal(1, 2);
        thrown.Rows[0].Errors.Should().Equal(PersonValidationError.FirstNameRequired);
        thrown.Rows[1].ContactMethodProblems.Should().Equal(new ContactMethodProblem(0, ContactMethodValidationError.EmailInvalid));
        thrown.Message.Should().NotContain("not-an-email").And.NotContain("Bea");
        await using var fresh = CreateDbContext(database);
        (await fresh.People.AsNoTracking().CountAsync()).Should().Be(0);
        (await fresh.ContactMethods.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportAsync_rejects_a_future_birthday_in_the_users_time_zone()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        dbContext.UserProfiles.Add(new UserProfile { OwnerId = Owner, TimeZoneId = "Pacific/Pago_Pago" });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        // 2026-10-07 12:00 UTC is 8 October in Kiritimati but still 7 October on Pago Pago (UTC-11).
        var act = () => CreateService(dbContext).ImportAsync([Request("Ada", null, birthday: (10, 7, 2026)), Request("Bea", null, birthday: (10, 8, 2026))]);

        var thrown = (await act.Should().ThrowAsync<PeopleImportValidationException>()).Which;
        thrown.Rows.Should().ContainSingle().Which.Index.Should().Be(1);
        thrown.Rows[0].Errors.Should().Equal(PersonValidationError.BirthdayInTheFuture);

        // For someone in Kiritimati the same date is today.
        await using var kiritimati = CreateDbContext(NewDatabase());
        kiritimati.UserProfiles.Add(new UserProfile { OwnerId = Owner, TimeZoneId = "Pacific/Kiritimati" });
        await kiritimati.SaveChangesAsync();
        kiritimati.ChangeTracker.Clear();
        (await CreateService(kiritimati).ImportAsync([Request("Bea", null, birthday: (10, 8, 2026))])).Should().Be(1);
    }

    [Fact]
    public async Task ImportAsync_with_an_empty_list_or_a_null_entry_throws_ArgumentException()
    {
        await using var dbContext = CreateDbContext(NewDatabase());
        var service = CreateService(dbContext);

        await service.Invoking(s => s.ImportAsync([])).Should().ThrowAsync<ArgumentException>();
        await service.Invoking(s => s.ImportAsync([Request("Ada", null), null!])).Should().ThrowAsync<ArgumentException>();
        await service.Invoking(s => s.ImportAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ImportAsync_over_2000_people_throws()
    {
        await using var dbContext = CreateDbContext(NewDatabase());

        var act = () => CreateService(dbContext).ImportAsync(Enumerable.Range(0, ImportLimits.MaxPeople + 1).Select(i => Request($"P{i}", null)).ToList());

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await dbContext.People.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportAsync_rejects_a_contact_method_id()
    {
        await using var dbContext = CreateDbContext(NewDatabase());

        var act = () => CreateService(dbContext).ImportAsync([Request("Ada", null, contacts: [new ContactMethodInput(Guid.NewGuid(), ContactMethodKind.Email, null, "a@example.com")])]);

        await act.Should().ThrowAsync<ArgumentException>();
        (await dbContext.People.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportAsync_after_a_failed_save_leaves_nothing_tracked()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database, new FailOnceSaveChangesInterceptor());
        var service = CreateService(dbContext);

        var act = () => service.ImportAsync([Request("Ada", null), Request("Bea", null)]);
        await act.Should().ThrowAsync<InvalidOperationException>();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        // The next save inserts only its own person, not the two that failed.
        await CreatePeopleService(dbContext).CreateAsync(new CreatePersonRequest { FirstName = "Cleo" });

        await using var fresh = CreateDbContext(database);
        (await fresh.People.AsNoTracking().Select(p => p.FirstName).ToListAsync()).Should().Equal("Cleo");
    }

    [Fact]
    public async Task Mutations_leave_nothing_tracked()
    {
        await using var dbContext = CreateDbContext(NewDatabase());

        await CreateService(dbContext).ImportAsync([Request("Ada", null, contacts: [new ContactMethodInput(null, ContactMethodKind.Email, null, "a@example.com")])]);

        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAsync_creates_people_without_a_relationship_type_or_tags()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        await CreateService(dbContext).ImportAsync([Request("Ada", null)]);

        await using var fresh = CreateDbContext(database);
        var person = await CreatePeopleService(fresh).GetAsync((await fresh.People.AsNoTracking().SingleAsync()).Id);
        person!.RelationshipTypeId.Should().BeNull();
        person.Tags.Should().BeEmpty();
        person.HowWeMet.Should().BeNull();
        person.IsArchived.Should().BeFalse();
        person.LastContactedOn.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_and_ImportAsync_build_identical_rows()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var contacts = new[]
        {
            new ContactMethodInput(null, ContactMethodKind.Email, " Work ", " Ada@Example.com "),
            new ContactMethodInput(null, ContactMethodKind.Phone, null, "+44 (7700) 900-123"),
            new ContactMethodInput(null, ContactMethodKind.Social, "Twitter", "@Ada"),
        };

        await CreatePeopleService(dbContext).CreateAsync(new CreatePersonRequest
        {
            FirstName = "Same",
            LastName = "Person",
            Nickname = "Twin",
            BirthdayMonth = 4,
            BirthdayDay = 15,
            Details = " Notes ",
            ContactMethods = contacts,
        });
        await CreateService(dbContext).ImportAsync([Request("Same", "Person", nickname: "Twin", birthday: (4, 15, null), details: " Notes ", contacts: contacts)]);

        await using var fresh = CreateDbContext(database);
        var people = await fresh.People.AsNoTracking().Include(p => p.ContactMethods).OrderBy(p => p.CreatedAtUtc).ToListAsync();
        people.Should().HaveCount(2);
        people[0].Should().BeEquivalentTo(people[1], options => options
            .Excluding(p => p.Id).Excluding(p => p.CreatedAtUtc).Excluding(p => p.UpdatedAtUtc).Excluding(p => p.ContactMethods));
        var left = people[0].ContactMethods.OrderBy(c => c.SortOrder).ToList();
        var right = people[1].ContactMethods.OrderBy(c => c.SortOrder).ToList();
        left.Should().HaveCount(3);
        left.Select(c => (c.Kind, c.Label, c.Value, c.NormalizedValue, c.SortOrder))
            .Should().Equal(right.Select(c => (c.Kind, c.Label, c.Value, c.NormalizedValue, c.SortOrder)));
    }

    [Fact]
    public async Task ImportAsync_without_a_user_throws()
    {
        await using var dbContext = CreateDbContext(NewDatabase());

        var act = () => new PeopleImportService(dbContext, new FakeCurrentUser(null), new FakeTimeProvider(Now)).ImportAsync([Request("Ada", null)]);

        await act.Should().ThrowAsync<UnauthenticatedUserException>();
    }

    // ---- PreviewAsync ----------------------------------------------------------------------

    [Fact]
    public async Task PreviewAsync_flags_existing_people_by_name_email_and_phone_including_archived()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        var peopleService = CreatePeopleService(dbContext);
        var byName = await peopleService.CreateAsync(new CreatePersonRequest { FirstName = "Ada", LastName = "Lovelace" });
        var byEmail = await peopleService.CreateAsync(new CreatePersonRequest
        {
            FirstName = "Mail",
            LastName = "Holder",
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "shared@example.com")],
        });
        var byPhone = await peopleService.CreateAsync(new CreatePersonRequest
        {
            FirstName = "Phone",
            LastName = "Holder",
            ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Phone, null, "+44 7700 900123")],
        });
        var archived = await peopleService.CreateAsync(new CreatePersonRequest { FirstName = "Gone", LastName = "Away" });
        await peopleService.ArchiveAsync(archived.Id);

        var read = new ImportReadResult(
        [
            Draft(2, "Ada", "Lovelace"),
            Draft(3, "Someone", "Else", new ImportContactDraft(ContactMethodKind.Email, null, "SHARED@example.com")),
            Draft(4, "Another", "Person", new ImportContactDraft(ContactMethodKind.Phone, null, "07700 900123")),
            Draft(5, "Gone", "Away"),
            Draft(6, "Totally", "New"),
        ], 5, false, 0);

        var preview = await CreateService(dbContext).PreviewAsync(read);

        preview.Candidates.Select(c => c.ExistingMatches.Select(m => m.Id).ToArray()).Should().SatisfyRespectively(
            a => a.Should().Equal(byName.Id),
            b => b.Should().Equal(byEmail.Id),
            c => c.Should().Equal(byPhone.Id),
            d => d.Should().Equal(archived.Id),
            e => e.Should().BeEmpty());
        preview.Candidates[3].ExistingMatches.Single().IsArchived.Should().BeTrue();
        preview.Candidates[4].SelectedByDefault.Should().BeTrue();
    }

    [Fact]
    public async Task PreviewAsync_writes_nothing()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        await CreatePeopleService(dbContext).CreateAsync(new CreatePersonRequest { FirstName = "Ada" });
        var counter = new CountingSaveChangesInterceptor();
        await using var watched = CreateDbContext(database, counter);

        await CreateService(watched).PreviewAsync(new ImportReadResult([Draft(2, "Ada", null), Draft(3, "Bea", null)], 2, false, 0));

        counter.Saves.Should().Be(0);
        watched.ChangeTracker.Entries().Should().BeEmpty();
        (await watched.People.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PreviewAsync_uses_today_in_the_users_time_zone()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        dbContext.UserProfiles.Add(new UserProfile { OwnerId = Owner, TimeZoneId = "Pacific/Pago_Pago" });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var preview = await CreateService(dbContext).PreviewAsync(new ImportReadResult(
            [new ImportPersonDraft(2, "Ada", null, null, new ImportBirthdayDraft(10, 8, 2026), false, null, [])], 1, false, 0));

        preview.Candidates.Single().Problems.Should().Equal(ImportProblem.BirthdayInTheFuture);
    }

    [Fact]
    public async Task PreviewAsync_without_a_user_throws()
    {
        await using var dbContext = CreateDbContext(NewDatabase());

        var act = () => new PeopleImportService(dbContext, new FakeCurrentUser(null), new FakeTimeProvider(Now))
            .PreviewAsync(new ImportReadResult([], 0, false, 0));

        await act.Should().ThrowAsync<UnauthenticatedUserException>();
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static ImportPersonDraft Draft(int row, string first, string? last, params ImportContactDraft[] contacts) =>
        new(row, first, last, null, null, false, null, contacts);

    private static ImportPersonRequest Request(
        string first,
        string? last,
        string? nickname = null,
        (int Month, int Day, int? Year)? birthday = null,
        string? details = null,
        IReadOnlyList<ContactMethodInput>? contacts = null) =>
        new()
        {
            FirstName = first,
            LastName = last,
            Nickname = nickname,
            BirthdayMonth = birthday?.Month,
            BirthdayDay = birthday?.Day,
            BirthdayYear = birthday?.Year,
            Details = details,
            ContactMethods = contacts ?? [],
        };

    private static DbContextOptions<RelioDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static RelioDbContext CreateDbContext(DbContextOptions<RelioDbContext> database, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var options = interceptors.Length == 0
            ? database
            : new DbContextOptionsBuilder<RelioDbContext>(database).AddInterceptors(interceptors).Options;
        return new RelioDbContext(options, new FakeTimeProvider(Now), FieldProtector);
    }

    private static PeopleImportService CreateService(RelioDbContext dbContext) =>
        new(dbContext, new FakeCurrentUser(Owner), new FakeTimeProvider(Now));

    private static PeopleService CreatePeopleService(RelioDbContext dbContext) =>
        new(dbContext, new FakeCurrentUser(Owner), new FakeTimeProvider(Now));
}
