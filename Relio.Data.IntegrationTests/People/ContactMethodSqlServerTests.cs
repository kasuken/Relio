using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Relio.Application.People;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.People;

/// <summary>
/// What only a real SQL Server can prove for issue #24: the contact method table the migration
/// builds (check constraint, cascade, indexes, column types), the case-insensitive unique tag name,
/// real round trips of the full-list diff, and the race where somebody else creates the tag a save
/// was about to create. The InMemory provider enforces none of the first four.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ContactMethodSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task CreateAsync_and_GetAsync_round_trip_contact_methods_and_tags_in_order()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var service = TestDataFactory.CreateService(dbContext, owner);

        var created = await service.CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            NewTagNames = ["Mentor", "chess"],
            ContactMethods =
            [
                new ContactMethodInput(null, ContactMethodKind.Phone, "Mobile", "+44 (7700) 900-123"),
                new ContactMethodInput(null, ContactMethodKind.Email, null, "Ada@Example.com"),
                new ContactMethodInput(null, ContactMethodKind.Address, "Home", "12 Example Square\nLondon"),
            ],
        });

        await using var fresh = fixture.CreateDbContext();
        var read = await TestDataFactory.CreateService(fresh, owner).GetAsync(created.Id);

        read!.ContactMethods.Select(c => (c.Kind, c.Label, c.Value, c.NormalizedValue, c.SortOrder)).Should().Equal(
            (ContactMethodKind.Phone, "Mobile", "+44 (7700) 900-123", "+447700900123", 0),
            (ContactMethodKind.Email, null, "Ada@Example.com", "ada@example.com", 1),
            (ContactMethodKind.Address, "Home", "12 Example Square\nLondon", "12 example square london", 2));
        read.ContactMethods.Should().OnlyContain(c => c.OwnerId == owner && c.PersonId == created.Id);
        read.Tags.Select(t => t.Name).Should().Equal("chess", "Mentor");
    }

    [SqlServerFact]
    public async Task UpdateAsync_adds_edits_and_removes_contact_methods_for_real()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var service = TestDataFactory.CreateService(dbContext, owner);
        var person = await service.CreateAsync(new CreatePersonRequest
        {
            FirstName = "Ada",
            ContactMethods =
            [
                new ContactMethodInput(null, ContactMethodKind.Email, null, "one@example.com"),
                new ContactMethodInput(null, ContactMethodKind.Email, null, "two@example.com"),
            ],
        });
        var stored = (await service.GetAsync(person.Id))!.ContactMethods.OrderBy(c => c.SortOrder).ToList();

        await service.UpdateAsync(person.Id, new UpdatePersonRequest
        {
            FirstName = "Ada",
            ContactMethods =
            [
                new ContactMethodInput(null, ContactMethodKind.Phone, null, "+44 7700 900123"),
                new ContactMethodInput(stored[0].Id, ContactMethodKind.Email, "Work", "uno@example.com"),
            ],
        });

        await using var fresh = fixture.CreateDbContext();
        var after = await fresh.ContactMethods.AsNoTracking()
            .Where(c => c.OwnerId == owner).OrderBy(c => c.SortOrder).ToListAsync();
        after.Select(c => c.Value).Should().Equal("+44 7700 900123", "uno@example.com");
        after[1].Id.Should().Be(stored[0].Id);
        after.Select(c => c.Id).Should().NotContain(stored[1].Id);
    }

    [SqlServerFact]
    public async Task Deleting_a_person_deletes_their_contact_methods_and_tag_links_in_the_database()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid personId;
        await using (var setup = fixture.CreateDbContext())
        {
            var person = await TestDataFactory.CreateService(setup, owner).CreateAsync(new CreatePersonRequest
            {
                FirstName = "Ada",
                NewTagNames = ["Chess"],
                ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "ada@example.com")],
            });
            personId = person.Id;
        }

        // A bulk delete bypasses the change tracker, so only the database's own cascade can clean up.
        await using (var delete = fixture.CreateDbContext())
        {
            (await delete.People.Where(p => p.Id == personId).ExecuteDeleteAsync()).Should().Be(1);
        }

        await using var verify = fixture.CreateDbContext();
        (await verify.ContactMethods.CountAsync(c => c.PersonId == personId)).Should().Be(0);
        (await verify.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM PersonTags WHERE PeopleId = {personId}").SingleAsync())
            .Should().Be(0);
        (await verify.Tags.CountAsync(t => t.OwnerId == owner)).Should().Be(1, "deleting a person never deletes their tags");
    }

    [SqlServerFact]
    public async Task A_contact_method_kind_outside_the_list_is_rejected_by_the_database()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Ada");

        var act = () => InsertRawAsync(dbContext, owner, personId, "Fax");

        await act.Should().ThrowAsync<SqlException>().WithMessage("*CK_ContactMethods_Kind*");
        await InsertRawAsync(dbContext, owner, personId, "Email");
    }

    [SqlServerFact]
    public async Task Contact_method_kind_is_stored_as_text()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Ada");
        var id = await TestDataFactory.CreateContactMethodAsync(dbContext, owner, personId, "+44 7700 900123", ContactMethodKind.Phone);

        var stored = await dbContext.Database.SqlQuery<string>($"SELECT [Kind] AS [Value] FROM ContactMethods WHERE Id = {id}").SingleAsync();

        stored.Should().Be("Phone");
    }

    [SqlServerTheory]
    [InlineData("IX_ContactMethods_OwnerId_PersonId", "OwnerId,PersonId")]
    [InlineData("IX_ContactMethods_OwnerId_NormalizedValue", "OwnerId,NormalizedValue")]
    [InlineData("IX_ContactMethods_PersonId", "PersonId")]
    public async Task Contact_method_indexes_exist_with_their_columns_in_order(string indexName, string expectedColumns)
    {
        await using var dbContext = fixture.CreateDbContext();

        var columns = await dbContext.Database.SqlQuery<string>($"""
            SELECT c.name AS [Value]
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID('dbo.ContactMethods') AND i.name = {indexName} AND ic.is_included_column = 0
            ORDER BY ic.key_ordinal
            """).ToListAsync();

        string.Join(",", columns).Should().Be(expectedColumns);
    }

    [SqlServerFact]
    public async Task The_widest_possible_contact_method_still_fits_the_index_key_limit()
    {
        await using var dbContext = fixture.CreateDbContext();
        var longestOwnerId = await TestDataFactory.CreateOwnerAsync(fixture, new string('o', 450));
        var personId = Guid.NewGuid();
        dbContext.People.Add(new Person { Id = personId, OwnerId = longestOwnerId, FirstName = "Ada" });
        await dbContext.SaveChangesAsync();
        dbContext.ContactMethods.Add(new ContactMethod
        {
            OwnerId = longestOwnerId,
            PersonId = personId,
            Kind = ContactMethodKind.Other,
            Label = new string('l', ContactMethod.LabelMaxLength),
            Value = new string('v', ContactMethod.ValueMaxLength),
            NormalizedValue = new string('n', ContactMethod.NormalizedValueMaxLength),
        });

        // 450 + 300 characters of nvarchar is the largest (OwnerId, NormalizedValue) key there can be.
        var act = () => dbContext.SaveChangesAsync();

        await act.Should().NotThrowAsync();
        await dbContext.ContactMethods.Where(c => c.OwnerId == longestOwnerId).ExecuteDeleteAsync();
        await dbContext.People.Where(p => p.Id == personId).ExecuteDeleteAsync();
    }

    [SqlServerFact]
    public async Task A_tag_name_is_unique_per_owner_case_insensitively()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        await TestDataFactory.CreateTagAsync(dbContext, owner, "Chess");

        dbContext.Tags.Add(new Tag { OwnerId = owner, Name = "chess" });
        var act = () => dbContext.SaveChangesAsync();

        // The database's collation, not application code, is what makes "chess" the same as "Chess"
        // - and why the service matches names itself first: InMemory would accept both.
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [SqlServerFact]
    public async Task UpdateAsync_matches_an_existing_tag_case_insensitively()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var existingId = await TestDataFactory.CreateTagAsync(dbContext, owner, "Climbing");
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Ada");

        var updated = await TestDataFactory.CreateService(dbContext, owner).UpdateAsync(
            personId, new UpdatePersonRequest { FirstName = "Ada", NewTagNames = ["CLIMBING"] });

        updated.Should().BeTrue();
        await using var fresh = fixture.CreateDbContext();
        (await fresh.Tags.CountAsync(t => t.OwnerId == owner)).Should().Be(1);
        var person = await fresh.People.AsNoTracking().Include(p => p.Tags).SingleAsync(p => p.Id == personId);
        person.Tags.Should().ContainSingle().Which.Id.Should().Be(existingId);
    }

    [SqlServerFact]
    public async Task UpdateAsync_creates_a_new_tag_with_the_person_in_one_save()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Ada");

        await TestDataFactory.CreateService(dbContext, owner).UpdateAsync(
            personId, new UpdatePersonRequest { FirstName = "Ada", NewTagNames = ["Rock   climbing", "Chess"] });

        await using var fresh = fixture.CreateDbContext();
        var person = await fresh.People.AsNoTracking().Include(p => p.Tags).SingleAsync(p => p.Id == personId);
        person.Tags.Select(t => t.Name).Should().BeEquivalentTo("Rock climbing", "Chess");
        person.Tags.Should().OnlyContain(t => t.OwnerId == owner);
    }

    [SqlServerFact]
    public async Task UpdateAsync_reports_a_tag_name_taken_at_the_same_moment_as_a_conflict_and_saves_nothing()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid personId;
        await using (var setup = fixture.CreateDbContext())
        {
            personId = await TestDataFactory.CreatePersonAsync(setup, owner, "Ada");
        }

        // The service reads the user's tags (no "Climbing" yet), decides to create it, and just
        // before its save another request creates "climbing" - which the unique index then refuses.
        var interceptor = new InsertTagOnFirstSaveInterceptor(fixture, owner, "climbing");
        await using var dbContext = fixture.CreateDbContext(interceptor);
        var act = () => TestDataFactory.CreateService(dbContext, owner).UpdateAsync(
            personId,
            new UpdatePersonRequest
            {
                FirstName = "Changed",
                NewTagNames = ["Climbing"],
                ContactMethods = [new ContactMethodInput(null, ContactMethodKind.Email, null, "ada@example.com")],
            });

        var exception = (await act.Should().ThrowAsync<PersonValidationException>()).Which;

        exception.Errors.Should().Equal(PersonValidationError.TagNameConflict);
        exception.Message.Should().NotContain("limbing");
        interceptor.Fired.Should().BeTrue();
        await using var fresh = fixture.CreateDbContext();
        var person = await fresh.People.AsNoTracking().Include(p => p.Tags).Include(p => p.ContactMethods)
            .SingleAsync(p => p.Id == personId);
        person.FirstName.Should().Be("Ada", "the whole save was rolled back");
        person.Tags.Should().BeEmpty();
        person.ContactMethods.Should().BeEmpty();
        (await fresh.Tags.Where(t => t.OwnerId == owner).Select(t => t.Name).ToListAsync())
            .Should().Equal("climbing");

        // Asking again now finds the tag by name and attaches it - which is what the form does.
        await using var retry = fixture.CreateDbContext();
        (await TestDataFactory.CreateService(retry, owner).UpdateAsync(
            personId, new UpdatePersonRequest { FirstName = "Ada", NewTagNames = ["Climbing"] })).Should().BeTrue();
        await using var verify = fixture.CreateDbContext();
        (await verify.Tags.CountAsync(t => t.OwnerId == owner)).Should().Be(1);
    }

    private static Task<int> InsertRawAsync(RelioDbContext dbContext, string ownerId, Guid personId, string kind) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ContactMethods (Id, PersonId, OwnerId, Kind, Label, Value, NormalizedValue, SortOrder, CreatedAtUtc, UpdatedAtUtc)
            VALUES ({Guid.NewGuid()}, {personId}, {ownerId}, {kind}, NULL, N'x', N'x', 0, SYSUTCDATETIME(), SYSUTCDATETIME())
            """);
}
