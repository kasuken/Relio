using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.People;
using Relio.Application.People.Import;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// Cross-user isolation for <see cref="PeopleImportService"/> (issue #29): imported people belong to
/// the importing user alone, and a preview never matches against, or reveals, another user's people.
/// InMemory; the same scenarios run on SQL Server in <c>PeopleImportSqlServerTests</c>.
/// </summary>
public class PeopleImportServiceOwnershipTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ImportAsync_creates_people_owned_by_the_current_user_only()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);

        await CreateImportService(dbContext, UserA).ImportAsync(
        [
            new ImportPersonRequest
            {
                FirstName = "Ada",
                ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "ada@example.com")],
            },
        ]);

        await using var fresh = CreateDbContext(database);
        (await fresh.People.AsNoTracking().ToListAsync()).Should().ContainSingle().Which.OwnerId.Should().Be(UserA);
        (await fresh.ContactMethods.AsNoTracking().ToListAsync()).Should().OnlyContain(c => c.OwnerId == UserA);
        (await fresh.Tags.AsNoTracking().CountAsync()).Should().Be(0);
        (await fresh.RelationshipTypes.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PreviewAsync_never_matches_another_users_people_or_contact_methods()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        await CreatePeopleService(dbContext, UserB).CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            ContactMethods =
            [
                new ContactMethodInput(null, ContactMethodKind.Email, null, "ada@example.com"),
                new ContactMethodInput(null, ContactMethodKind.Phone, null, "+44 7700 900123"),
            ],
        });

        var preview = await CreateImportService(dbContext, UserA).PreviewAsync(new ImportReadResult(
        [
            new ImportPersonDraft(2, "Ada", "Lovelace", null, null, false, null,
                [new ImportContactDraft(ContactMethodKind.Email, null, "ada@example.com"), new ImportContactDraft(ContactMethodKind.Phone, null, "+44 7700 900123")]),
        ], 1, false, 0));

        preview.Candidates.Single().ExistingMatches.Should().BeEmpty();
        preview.Candidates.Single().SelectedByDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Another_user_cannot_see_imported_people()
    {
        var database = NewDatabase();
        await using var dbContext = CreateDbContext(database);
        await CreateImportService(dbContext, UserA).ImportAsync(
            [new ImportPersonRequest { FirstName = "Ada", ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "ada@example.com")] }]);
        await using var fresh = CreateDbContext(database);
        var importedId = (await fresh.People.AsNoTracking().SingleAsync()).Id;

        var asB = CreatePeopleService(fresh, UserB);

        (await asB.GetAsync(importedId)).Should().BeNull();
        (await asB.ListAsync(includeArchived: true)).Should().BeEmpty();
        (await asB.ListPageAsync(new PeopleListQuery { IncludeArchived = true })).People.Items.Should().BeEmpty();
        (await asB.FindPossibleDuplicatesAsync(new PossibleDuplicateQuery { FirstName = "Ada" })).Should().BeEmpty();
        (await CreateImportService(fresh, UserB).PreviewAsync(new ImportReadResult([new ImportPersonDraft(2, "Ada", null, null, null, false, null, [])], 1, false, 0)))
            .Candidates.Single().ExistingMatches.Should().BeEmpty();
    }

    private static DbContextOptions<RelioDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static RelioDbContext CreateDbContext(DbContextOptions<RelioDbContext> database) =>
        new(database, new FakeTimeProvider(Now), FieldProtector);

    private static PeopleImportService CreateImportService(RelioDbContext dbContext, string userId) =>
        new(dbContext, new FakeCurrentUser(userId), new FakeTimeProvider(Now));

    private static PeopleService CreatePeopleService(RelioDbContext dbContext, string userId) =>
        new(dbContext, new FakeCurrentUser(userId), new FakeTimeProvider(Now));
}
