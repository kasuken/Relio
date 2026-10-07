using Microsoft.EntityFrameworkCore;
using Relio.Application.Notes;
using Relio.Application.Ownership;
using Relio.Data.Encryption;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Notes;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Notes;

/// <summary>
/// SQL Server-only guarantees for notes: the migrated indexes, database cascade, real round trips
/// and the owner filters that the InMemory provider cannot prove at the database boundary.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class NoteSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerTheory]
    [InlineData("IX_Notes_OwnerId_PersonId_CreatedAtUtc_Id", "OwnerId,PersonId,CreatedAtUtc,Id")]
    [InlineData("IX_Notes_OwnerId_PersonId_IsPinned", "OwnerId,PersonId,IsPinned")]
    public async Task Note_indexes_exist_with_the_requested_columns_in_order(string indexName, string expectedColumns)
    {
        await using var dbContext = fixture.CreateDbContext();

        var columns = await dbContext.Database.SqlQuery<string>($"""
            SELECT c.name AS [Value]
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID('dbo.Notes') AND i.name = {indexName} AND ic.is_included_column = 0
            ORDER BY ic.key_ordinal
            """).ToListAsync();

        string.Join(",", columns).Should().Be(expectedColumns);
    }

    [SqlServerFact]
    public async Task Note_creation_and_reads_round_trip_with_sql_server()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid personId;
        Guid noteId;
        await using (var setup = fixture.CreateDbContext())
        {
            personId = await TestDataFactory.CreatePersonAsync(setup, owner, "Ada");
            var created = await CreateService(setup, owner).CreateAsync(
                new CreateNoteRequest(personId, "  Remember the kind gesture.\nAnd the follow-up.  ", IsPinned: true));
            noteId = created.Id;
            created.Text.Should().Be("Remember the kind gesture.\nAnd the follow-up.");
            created.OwnerId.Should().Be(owner);
            created.PersonId.Should().Be(personId);
            created.IsPinned.Should().BeTrue();
        }

        await using var fresh = fixture.CreateDbContext();
        var service = CreateService(fresh, owner);
        var note = await service.GetAsync(noteId);
        var pinned = await service.ListPinnedAsync(personId);

        note.Should().NotBeNull();
        note!.Text.Should().Be("Remember the kind gesture.\nAnd the follow-up.");
        pinned.Select(item => item.Id).Should().Equal(noteId);
        var storedText = await fresh.Database.SqlQuery<string>(
            $"SELECT [Text] AS [Value] FROM dbo.Notes WHERE [Id] = {noteId}").SingleAsync();
        storedText.Should().NotContain(note.Text);
        FieldProtector.Unprotect(storedText, ProtectedFieldPurposes.NoteText).Should().Be(note.Text);
    }

    [SqlServerFact]
    public async Task Deleting_a_person_directly_in_sql_server_cascades_to_notes()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid personId;
        Guid noteId;
        await using (var setup = fixture.CreateDbContext())
        {
            personId = await TestDataFactory.CreatePersonAsync(setup, owner, "Ada");
            noteId = (await CreateService(setup, owner).CreateAsync(
                new CreateNoteRequest(personId, "This is kept until the person is removed."))).Id;
        }

        // A bulk delete bypasses EF's tracked cascade, proving the migrated SQL foreign key.
        await using (var delete = fixture.CreateDbContext())
        {
            (await delete.People.Where(person => person.Id == personId && person.OwnerId == owner)
                .ExecuteDeleteAsync()).Should().Be(1);
        }

        await using var verify = fixture.CreateDbContext();
        (await verify.Set<Note>().AsNoTracking().CountAsync(note => note.Id == noteId)).Should().Be(0);
    }

    [SqlServerFact]
    public async Task Notes_are_isolated_by_owner_for_all_reads_and_mutations()
    {
        var ownerA = await TestDataFactory.CreateOwnerAsync(fixture);
        var ownerB = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid personA;
        Guid personB;
        Guid noteA;
        await using (var setup = fixture.CreateDbContext())
        {
            personA = await TestDataFactory.CreatePersonAsync(setup, ownerA, "Ada");
            personB = await TestDataFactory.CreatePersonAsync(setup, ownerB, "Grace");
            noteA = (await CreateService(setup, ownerA).CreateAsync(
                new CreateNoteRequest(personA, "Owner A only.", IsPinned: true))).Id;
        }

        await using var forB = fixture.CreateDbContext();
        var serviceB = CreateService(forB, ownerB);

        (await serviceB.GetAsync(noteA)).Should().BeNull();
        (await serviceB.ListPinnedAsync(personA)).Should().BeEmpty();
        (await serviceB.UpdateAsync(noteA, new UpdateNoteRequest("No access."))).Should().BeFalse();
        (await serviceB.SetPinnedAsync(noteA, false)).Should().BeFalse();
        (await serviceB.DeleteAsync(noteA)).Should().BeFalse();
        var foreignPerson = await Assert.ThrowsAsync<ForeignEntityNotOwnedException>(
            () => serviceB.CreateAsync(new CreateNoteRequest(personA, "No access.")));
        foreignPerson.EntityName.Should().Be("people");
        (await serviceB.ListPinnedAsync(personB)).Should().BeEmpty();

        await using var verify = fixture.CreateDbContext();
        var ownerANote = await CreateService(verify, ownerA).GetAsync(noteA);
        ownerANote.Should().NotBeNull();
        ownerANote!.Text.Should().Be("Owner A only.");
        ownerANote.IsPinned.Should().BeTrue();
    }

    private static NoteService CreateService(RelioDbContext dbContext, string? ownerId) =>
        new(dbContext, new FakeCurrentUser(ownerId));
}
