using Microsoft.EntityFrameworkCore;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.People;

/// <summary>
/// What only a real SQL Server can prove for issue #26: deleting a person really removes their
/// contact methods, notes, tag links and interaction participation in one save (and leaves the tags and
/// other people's links alone), shared interactions survive until their last participant is removed,
/// every foreign key that references <c>People</c> is <c>ON DELETE CASCADE</c> - the backstop behind
/// <c>PeopleService.RemoveDependentsAsync</c> - and archive/restore round trip through the real
/// <c>datetime2</c> column.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PersonDeleteSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task DeleteAsync_removes_the_person_their_contact_methods_and_tag_links_and_keeps_the_tags()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = TestDataFactory.NewOwnerId();
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Ada");
        var otherPersonId = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Grace");
        var chessId = await TestDataFactory.CreateTagAsync(dbContext, owner, "Chess");
        var climbingId = await TestDataFactory.CreateTagAsync(dbContext, owner, "Climbing");
        dbContext.ChangeTracker.Clear();
        await TestDataFactory.TagPersonAsync(dbContext, personId, chessId);
        await TestDataFactory.TagPersonAsync(dbContext, personId, climbingId);
        await TestDataFactory.TagPersonAsync(dbContext, otherPersonId, chessId);
        await TestDataFactory.CreateContactMethodAsync(dbContext, owner, personId, "ada@example.com");
        await TestDataFactory.CreateContactMethodAsync(dbContext, owner, personId, "+44 7700 900123", ContactMethodKind.Phone, 1);
        await TestDataFactory.CreateContactMethodAsync(dbContext, owner, otherPersonId, "grace@example.com");
        await TestDataFactory.CreateNoteAsync(dbContext, owner, personId, "Ada's note.", isPinned: true);
        await TestDataFactory.CreateNoteAsync(dbContext, owner, otherPersonId, "Grace's note.");

        var deleted = await TestDataFactory.CreateService(dbContext, owner).DeleteAsync(personId);

        deleted.Should().BeTrue();
        await using var verify = fixture.CreateDbContext();
        (await verify.People.AnyAsync(p => p.Id == personId)).Should().BeFalse();
        (await verify.ContactMethods.CountAsync(c => c.PersonId == personId)).Should().Be(0);
        (await verify.Notes.CountAsync(note => note.PersonId == personId)).Should().Be(0);
        (await verify.Notes.CountAsync(note => note.PersonId == otherPersonId)).Should().Be(1);
        (await CountTagLinksAsync(verify, personId)).Should().Be(0);
        (await verify.Tags.CountAsync(t => t.OwnerId == owner)).Should().Be(2, "deleting a person never deletes their tags");
        (await CountTagLinksAsync(verify, otherPersonId)).Should().Be(1, "another person's link to the same tag stays");
        (await verify.ContactMethods.CountAsync(c => c.PersonId == otherPersonId)).Should().Be(1);
        (await verify.People.AnyAsync(p => p.Id == otherPersonId)).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task DeleteAsync_deletes_an_archived_person_and_a_second_delete_finds_nobody()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = TestDataFactory.NewOwnerId();
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Ada", isArchived: true);
        var service = TestDataFactory.CreateService(dbContext, owner);

        (await service.DeleteAsync(personId)).Should().BeTrue();
        (await service.DeleteAsync(personId)).Should().BeFalse();

        await using var verify = fixture.CreateDbContext();
        (await verify.People.AnyAsync(p => p.Id == personId)).Should().BeFalse();
    }

    [SqlServerFact]
    public async Task DeleteAsync_preserves_shared_interactions_until_the_last_participant_is_deleted()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = TestDataFactory.NewOwnerId();
        var firstPersonId = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Ada");
        var secondPersonId = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Grace");
        var today = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
        var interactions = TestDataFactory.CreateInteractionService(dbContext, owner);
        var sharedId = await interactions.CreateAsync(new Relio.Application.Interactions.CreateInteractionRequest
        {
            ProfilePersonId = firstPersonId,
            OccurredOn = today,
            Kind = InteractionKind.Meeting,
            Description = "A shared meeting",
            ParticipantIds = [firstPersonId, secondPersonId],
        });
        var privateId = await interactions.CreateAsync(new Relio.Application.Interactions.CreateInteractionRequest
        {
            ProfilePersonId = firstPersonId,
            OccurredOn = today,
            Kind = InteractionKind.Call,
            Description = "A private call",
            ParticipantIds = [firstPersonId],
        });

        (await TestDataFactory.CreateService(dbContext, owner).DeleteAsync(firstPersonId)).Should().BeTrue();
        await using (var verify = fixture.CreateDbContext())
        {
            (await verify.Interactions
                .Where(interaction => interaction.OwnerId == owner)
                .Select(interaction => interaction.Id)
                .ToListAsync()).Should().Equal(sharedId);
            (await verify.InteractionParticipants
                .Where(participant => participant.OwnerId == owner)
                .Select(participant => participant.PersonId)
                .ToListAsync())
                .Should().ContainSingle().Which.Should().Be(secondPersonId);
            (await verify.Interactions.AnyAsync(interaction => interaction.Id == privateId)).Should().BeFalse();
        }

        (await TestDataFactory.CreateService(dbContext, owner).DeleteAsync(secondPersonId)).Should().BeTrue();
        await using var final = fixture.CreateDbContext();
        (await final.Interactions.CountAsync(interaction => interaction.OwnerId == owner)).Should().Be(0);
        (await final.InteractionParticipants.CountAsync(participant => participant.OwnerId == owner)).Should().Be(0);
    }

    [SqlServerFact]
    public async Task DeleteAsync_for_another_users_person_returns_false_and_deletes_nothing()
    {
        await using var dbContext = fixture.CreateDbContext();
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, ownerA, "Alice");
        await TestDataFactory.CreateContactMethodAsync(dbContext, ownerA, personId, "alice@example.com");

        var deleted = await TestDataFactory.CreateService(dbContext, ownerB).DeleteAsync(personId);

        deleted.Should().BeFalse();
        await using var verify = fixture.CreateDbContext();
        (await verify.People.AnyAsync(p => p.Id == personId)).Should().BeTrue();
        (await verify.ContactMethods.CountAsync(c => c.PersonId == personId)).Should().Be(1);
    }

    [SqlServerFact]
    public async Task Every_foreign_key_that_references_People_cascades()
    {
        await using var dbContext = fixture.CreateDbContext();

        var foreignKeys = await dbContext.Database.SqlQuery<string>($"""
            SELECT CONVERT(nvarchar(300), fk.name) COLLATE DATABASE_DEFAULT + N':'
                 + CONVERT(nvarchar(60), fk.delete_referential_action_desc) COLLATE DATABASE_DEFAULT AS [Value]
            FROM sys.foreign_keys fk
            WHERE fk.referenced_object_id = OBJECT_ID(N'dbo.People')
            """).ToListAsync();

        foreignKeys.Should().NotBeEmpty();
        foreignKeys.Should().OnlyContain(
            foreignKey => foreignKey.EndsWith(":CASCADE"),
            "a child that does not cascade would block deleting its person on SQL Server (or be orphaned)");
        foreignKeys.Should().Contain("FK_PersonTags_People_PeopleId:CASCADE");
        foreignKeys.Should().Contain("FK_ContactMethods_People_PersonId:CASCADE");
        foreignKeys.Should().Contain("FK_InteractionParticipants_People_PersonId:CASCADE");
        foreignKeys.Should().Contain("FK_Notes_People_PersonId:CASCADE");
        foreignKeys.Should().Contain("FK_Reminders_People_PersonId:CASCADE");
    }

    [SqlServerFact]
    public async Task Archive_and_restore_round_trip_on_SQL_Server()
    {
        await using var dbContext = fixture.CreateDbContext();
        var owner = TestDataFactory.NewOwnerId();
        var personId = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Ada");
        var service = TestDataFactory.CreateService(dbContext, owner);

        (await service.ArchiveAsync(personId)).Should().BeTrue();
        var archived = await service.GetAsync(personId);
        archived!.IsArchived.Should().BeTrue();
        archived.ArchivedAtUtc.Should().NotBeNull().And.BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
        (await service.ListAsync()).Should().BeEmpty();

        (await service.RestoreAsync(personId)).Should().BeTrue();
        var restored = await service.GetAsync(personId);
        restored!.IsArchived.Should().BeFalse();
        restored.ArchivedAtUtc.Should().BeNull();
        (await service.ListAsync()).Should().ContainSingle();
    }

    private static Task<int> CountTagLinksAsync(RelioDbContext dbContext, Guid personId) =>
        dbContext.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [PersonTags] WHERE [PeopleId] = {personId}")
            .SingleAsync();
}
