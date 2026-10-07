using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Relio.Application.People;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.People;

/// <summary>
/// What only a real SQL Server can prove for issue #28: merging moves notes, contact methods and tag links
/// and deletes the duplicate for real (the moved rows survive the removal of the person they used to
/// belong to), everything is one transaction that rolls back completely when any statement fails, a
/// duplicate deleted mid-merge is reported as not found, and every foreign key that references
/// <c>People</c> is one <c>PersonMergeService.MoveDependentsAsync</c> knows about.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PersonMergeSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task MergeAsync_moves_everything_and_deletes_the_duplicate_on_SQL_Server()
    {
        var owner = TestDataFactory.NewOwnerId();
        var seeded = await SeedAsync(owner);
        await using (var notes = fixture.CreateDbContext())
        {
            await TestDataFactory.CreateNoteAsync(notes, owner, seeded.PrimaryId, "Primary note.", isPinned: true);
            await TestDataFactory.CreateNoteAsync(notes, owner, seeded.DuplicateId, "Duplicate note.");
        }
        await using (var reminders = fixture.CreateDbContext())
        {
            reminders.Reminders.Add(new Reminder
            {
                OwnerId = owner,
                PersonId = seeded.DuplicateId,
                Title = "Call after the trip",
                DueDate = new DateOnly(2026, 10, 10),
            });
            await reminders.SaveChangesAsync();
        }

        await using var dbContext = fixture.CreateDbContext();

        var outcome = await TestDataFactory.CreatePersonMergeService(dbContext, owner)
            .MergeAsync(new MergePeopleRequest { PrimaryId = seeded.PrimaryId, DuplicateId = seeded.DuplicateId });

        outcome.Should().Be(MergeOutcome.Merged);
        await using var verify = fixture.CreateDbContext();
        (await verify.People.AnyAsync(p => p.Id == seeded.DuplicateId)).Should().BeFalse("the duplicate is gone");
        var rows = await verify.ContactMethods.Where(c => c.OwnerId == owner).OrderBy(c => c.SortOrder).ToListAsync();
        rows.Should().OnlyContain(c => c.PersonId == seeded.PrimaryId, "every kept row now belongs to the primary");
        rows.Select(c => c.Value).Should().Equal("john@example.com", "+44 7700 900123");
        rows.Select(c => c.SortOrder).Should().Equal(0, 1);
        rows[0].Label.Should().Be("Work", "the primary's row took the label of the repeated one");
        rows.Select(c => c.Id).Should().Contain(seeded.PhoneId, "the moved row survives the delete of its old person");
        rows.Select(c => c.Id).Should().NotContain(seeded.RepeatedEmailId);
        (await CountTagLinksAsync(verify, seeded.PrimaryId)).Should().Be(3, "the union of Chess, Climbing and Sailing");
        (await CountTagLinksAsync(verify, seeded.DuplicateId)).Should().Be(0);
        (await CountTagLinksAsync(verify, seeded.OtherId)).Should().Be(1, "another person's link is untouched");
        (await verify.Tags.CountAsync(t => t.OwnerId == owner)).Should().Be(3, "tag rows are never deleted");
        (await verify.People.CountAsync(p => p.OwnerId == owner)).Should().Be(2);
        var notesAfterMerge = await verify.Notes.Where(note => note.OwnerId == owner).ToListAsync();
        notesAfterMerge.Should().HaveCount(2);
        notesAfterMerge.Should().OnlyContain(note => note.PersonId == seeded.PrimaryId);
        notesAfterMerge.Single(note => note.IsPinned).Text.Should().Be("Primary note.");
        notesAfterMerge.Single(note => !note.IsPinned).Text.Should().Be("Duplicate note.");
        var remindersAfterMerge = await verify.Reminders.Where(reminder => reminder.OwnerId == owner).ToListAsync();
        remindersAfterMerge.Should().ContainSingle();
        (remindersAfterMerge[0].PersonId, remindersAfterMerge[0].Title)
            .Should().Be((seeded.PrimaryId, "Call after the trip"));
        var sharedParticipants = await verify.InteractionParticipants
            .Where(participant => participant.InteractionId == seeded.SharedInteractionId)
            .Select(participant => participant.PersonId)
            .ToListAsync();
        sharedParticipants.Should().ContainSingle().Which.Should().Be(seeded.PrimaryId, "the duplicate's repeated participant row is removed");
        var movedParticipants = await verify.InteractionParticipants
            .Where(participant => participant.InteractionId == seeded.DuplicateOnlyInteractionId)
            .Select(participant => participant.PersonId)
            .ToListAsync();
        movedParticipants.Should().BeEquivalentTo(new[] { seeded.PrimaryId, seeded.OtherId });
        (await verify.Interactions.CountAsync(interaction => interaction.OwnerId == owner)).Should().Be(3);
    }

    [SqlServerFact]
    public async Task MergeAsync_applies_field_choices_and_the_later_last_contacted_date_on_SQL_Server()
    {
        var owner = TestDataFactory.NewOwnerId();
        var seeded = await SeedAsync(owner);
        await using var dbContext = fixture.CreateDbContext();

        await TestDataFactory.CreatePersonMergeService(dbContext, owner).MergeAsync(new MergePeopleRequest
        {
            PrimaryId = seeded.PrimaryId,
            DuplicateId = seeded.DuplicateId,
            FieldChoices = new Dictionary<MergeField, MergeFieldChoice>
            {
                [MergeField.Name] = MergeFieldChoice.Duplicate,
                [MergeField.Details] = MergeFieldChoice.Both,
            },
        });

        await using var verify = fixture.CreateDbContext();
        var merged = await verify.People.SingleAsync(p => p.Id == seeded.PrimaryId);
        (merged.FirstName, merged.LastName).Should().Be(("Jon", "Smythe"));
        merged.Details.Should().Be("Primary details.\n\nDuplicate details.");
        merged.LastContactedOn.Should().Be(new DateOnly(2026, 8, 1), "the value comes from the latest surviving interaction, not the stale profile columns");
        merged.IsArchived.Should().BeFalse("the active primary beats the archived duplicate by default");
    }

    [SqlServerFact]
    public async Task MergeAsync_runs_every_write_in_one_transaction()
    {
        var owner = TestDataFactory.NewOwnerId();
        var seeded = await SeedAsync(owner);
        var commands = new RecordingCommandInterceptor();
        var saves = new CountingSaveInterceptor();
        await using var dbContext = CreateOneStatementPerCommandContext(commands, saves);

        await TestDataFactory.CreatePersonMergeService(dbContext, owner)
            .MergeAsync(new MergePeopleRequest { PrimaryId = seeded.PrimaryId, DuplicateId = seeded.DuplicateId });

        saves.Saves.Should().Be(1, "one SaveChanges");
        commands.Writes.Should().HaveCountGreaterThan(2, "with one statement per command the merge is several commands");
        commands.Writes.Should().OnlyContain(write => write.Transaction != null, "every write is inside a transaction");
        commands.Writes.Select(write => write.Transaction).Distinct().Should().ContainSingle("and it is the same transaction");
    }

    [SqlServerFact]
    public async Task A_failure_in_the_middle_of_the_save_rolls_everything_back()
    {
        var owner = TestDataFactory.NewOwnerId();
        var seeded = await SeedAsync(owner);
        var failure = new FailOnPersonDeleteInterceptor();
        await using var dbContext = CreateOneStatementPerCommandContext(failure);

        var act = () => TestDataFactory.CreatePersonMergeService(dbContext, owner).MergeAsync(new MergePeopleRequest
        {
            PrimaryId = seeded.PrimaryId,
            DuplicateId = seeded.DuplicateId,
            FieldChoices = new Dictionary<MergeField, MergeFieldChoice> { [MergeField.Name] = MergeFieldChoice.Duplicate },
        });

        var exception = await act.Should().ThrowAsync<Exception>();
        exception.Which.ToString().Should().Contain("simulated failure");
        failure.WritesBeforeFailure.Should().BeGreaterThan(1, "the statements before the delete of the person did run, inside the transaction");
        dbContext.ChangeTracker.Entries().Should().BeEmpty();

        // Nothing at all changed: the primary's fields, the contact methods (including the one that would
        // have been deleted as a repeat and the ones that would have moved), the tag links, both people.
        await using var verify = fixture.CreateDbContext();
        var primary = await verify.People.SingleAsync(p => p.Id == seeded.PrimaryId);
        (primary.FirstName, primary.LastName).Should().Be(("John", "Smith"));
        (await verify.People.AnyAsync(p => p.Id == seeded.DuplicateId)).Should().BeTrue();
        var rows = await verify.ContactMethods.Where(c => c.OwnerId == owner).ToListAsync();
        rows.Should().HaveCount(3);
        rows.Where(c => c.PersonId == seeded.PrimaryId).Select(c => c.Id).Should().Equal([seeded.PrimaryEmailId]);
        rows.Where(c => c.PersonId == seeded.DuplicateId).Select(c => c.Id).Should().BeEquivalentTo([seeded.RepeatedEmailId, seeded.PhoneId]);
        rows.Single(c => c.Id == seeded.PrimaryEmailId).Label.Should().BeNull("the label fill was rolled back too");
        (await CountTagLinksAsync(verify, seeded.PrimaryId)).Should().Be(2);
        (await CountTagLinksAsync(verify, seeded.DuplicateId)).Should().Be(2);
    }

    [SqlServerFact]
    public async Task A_duplicate_deleted_during_the_merge_returns_NotFound_and_changes_nothing()
    {
        var owner = TestDataFactory.NewOwnerId();
        var seeded = await SeedAsync(owner);
        var deleteDuplicate = new DeletePersonOnFirstSaveInterceptor(fixture, seeded.DuplicateId);
        await using var dbContext = fixture.CreateDbContext(deleteDuplicate);

        var outcome = await TestDataFactory.CreatePersonMergeService(dbContext, owner)
            .MergeAsync(new MergePeopleRequest { PrimaryId = seeded.PrimaryId, DuplicateId = seeded.DuplicateId });

        deleteDuplicate.Fired.Should().BeTrue();
        outcome.Should().Be(MergeOutcome.NotFound);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
        await using var verify = fixture.CreateDbContext();
        var primary = await verify.People.SingleAsync(p => p.Id == seeded.PrimaryId);
        primary.Details.Should().Be("Primary details.", "the primary is unchanged");
        (await verify.ContactMethods.Where(c => c.PersonId == seeded.PrimaryId).Select(c => c.Id).ToListAsync())
            .Should().Equal([seeded.PrimaryEmailId], "no contact method was moved to the primary");
        (await CountTagLinksAsync(verify, seeded.PrimaryId)).Should().Be(2);
    }

    [SqlServerFact]
    public async Task Every_foreign_key_that_references_People_is_handled_by_merge()
    {
        await using var dbContext = fixture.CreateDbContext();

        var foreignKeys = await dbContext.Database.SqlQuery<string>($"""
            SELECT CONVERT(nvarchar(300), fk.name) COLLATE DATABASE_DEFAULT AS [Value]
            FROM sys.foreign_keys fk
            WHERE fk.referenced_object_id = OBJECT_ID(N'dbo.People')
            """).ToListAsync();

        foreignKeys.Should().BeEquivalentTo(
            [
                "FK_PersonTags_People_PeopleId",
                "FK_ContactMethods_People_PersonId",
                "FK_InteractionParticipants_People_PersonId",
                "FK_Notes_People_PersonId",
                "FK_Reminders_People_PersonId",
            ],
            "a new foreign key to People needs a line in PersonMergeService.MoveDependentsAsync (and in "
            + "PeopleService.RemoveDependentsAsync), and an entry here and in PersonMergeChecklistTests");
    }

    [SqlServerFact]
    public async Task MergeAsync_with_another_owners_duplicate_returns_NotFound_and_changes_nothing()
    {
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        var a = await SeedAsync(ownerA);
        var b = await SeedAsync(ownerB);
        await using var dbContext = fixture.CreateDbContext();

        var outcome = await TestDataFactory.CreatePersonMergeService(dbContext, ownerA)
            .MergeAsync(new MergePeopleRequest { PrimaryId = a.PrimaryId, DuplicateId = b.DuplicateId });

        outcome.Should().Be(MergeOutcome.NotFound);
        await using var verify = fixture.CreateDbContext();
        (await verify.People.AnyAsync(p => p.Id == b.DuplicateId)).Should().BeTrue();
        (await verify.ContactMethods.CountAsync(c => c.PersonId == b.DuplicateId)).Should().Be(2);
        (await CountTagLinksAsync(verify, b.DuplicateId)).Should().Be(2);
        (await verify.ContactMethods.CountAsync(c => c.PersonId == a.PrimaryId)).Should().Be(1);
    }

    [SqlServerFact]
    public async Task ListCandidatesAsync_suggests_duplicates_and_never_lists_another_owners_people()
    {
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        var a = await SeedAsync(ownerA);
        var b = await SeedAsync(ownerB);
        await using var dbContext = fixture.CreateDbContext();
        var service = TestDataFactory.CreatePersonMergeService(dbContext, ownerA);

        var candidates = await service.ListCandidatesAsync(a.PrimaryId);

        candidates!.Others.Select(p => p.Id).Should().BeEquivalentTo([a.DuplicateId, a.OtherId]);
        candidates.Suggestions.Select(s => s.Id).Should().Equal([a.DuplicateId]);
        candidates.Others.Select(p => p.Id).Should().NotContain([b.PrimaryId, b.DuplicateId, b.OtherId]);
        (await service.ListCandidatesAsync(b.PrimaryId)).Should().BeNull();
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private RelioDbContext CreateOneStatementPerCommandContext(params IInterceptor[] interceptors)
    {
        // MaxBatchSize(1): every INSERT, UPDATE and DELETE is its own command, so the writes of one
        // SaveChanges are visible one by one - and a failure can hit exactly one of them.
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseSqlServer(fixture.ConnectionString, sqlServer => sqlServer.MaxBatchSize(1))
            .AddInterceptors(interceptors)
            .Options;
        return new RelioDbContext(options, TimeProvider.System);
    }

    private static Task<int> CountTagLinksAsync(RelioDbContext dbContext, Guid personId) =>
        dbContext.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [PersonTags] WHERE [PeopleId] = {personId}")
            .SingleAsync();

    private sealed record Seeded(
        Guid PrimaryId,
        Guid DuplicateId,
        Guid OtherId,
        Guid PrimaryEmailId,
        Guid RepeatedEmailId,
        Guid PhoneId,
        Guid SharedInteractionId,
        Guid DuplicateOnlyInteractionId);

    /// <summary>
    /// John Smith (primary: "Primary details.", Chess and Climbing, john@example.com without a label), Jon
    /// Smythe (duplicate: archived, "Duplicate details.", stale last-contacted 1 August 2026, Climbing and
    /// Sailing, JOHN@example.com labelled Work, and a phone) and Grace with the Chess tag. Three shared
    /// interaction cases cover primary-only, duplicate-plus-primary, and duplicate-plus-third-person.
    /// </summary>
    private async Task<Seeded> SeedAsync(string owner)
    {
        await using var dbContext = fixture.CreateDbContext();
        var chess = await TestDataFactory.CreateTagAsync(dbContext, owner, "Chess");
        var climbing = await TestDataFactory.CreateTagAsync(dbContext, owner, "Climbing");
        var sailing = await TestDataFactory.CreateTagAsync(dbContext, owner, "Sailing");
        var primary = await TestDataFactory.CreatePersonAsync(dbContext, owner, "John", "Smith", lastContactedOn: new DateOnly(2026, 10, 5));
        var duplicate = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Jon", "Smythe", lastContactedOn: new DateOnly(2026, 8, 1), isArchived: true);
        var other = await TestDataFactory.CreatePersonAsync(dbContext, owner, "Grace");
        dbContext.ChangeTracker.Clear();

        foreach (var (person, details) in new[] { (primary, "Primary details."), (duplicate, "Duplicate details.") })
        {
            var row = await dbContext.People.SingleAsync(p => p.Id == person);
            row.Details = details;
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();
        }

        await TestDataFactory.TagPersonAsync(dbContext, primary, chess);
        await TestDataFactory.TagPersonAsync(dbContext, primary, climbing);
        await TestDataFactory.TagPersonAsync(dbContext, duplicate, climbing);
        await TestDataFactory.TagPersonAsync(dbContext, duplicate, sailing);
        await TestDataFactory.TagPersonAsync(dbContext, other, chess);
        var primaryEmail = await TestDataFactory.CreateContactMethodAsync(dbContext, owner, primary, "john@example.com");
        var repeatedEmail = await TestDataFactory.CreateContactMethodAsync(dbContext, owner, duplicate, "JOHN@example.com", label: "Work");
        var phone = await TestDataFactory.CreateContactMethodAsync(dbContext, owner, duplicate, "+44 7700 900123", ContactMethodKind.Phone, 1);

        dbContext.ChangeTracker.Clear();
        var primaryInteraction = new Interaction
        {
            OwnerId = owner,
            OccurredOn = new DateOnly(2026, 3, 1),
            Kind = InteractionKind.Call,
            Description = "A private call.",
        };
        var sharedInteraction = new Interaction
        {
            OwnerId = owner,
            OccurredOn = new DateOnly(2026, 8, 1),
            Kind = InteractionKind.Meeting,
            Description = "A private meeting.",
        };
        var duplicateOnlyInteraction = new Interaction
        {
            OwnerId = owner,
            OccurredOn = new DateOnly(2026, 7, 15),
            Kind = InteractionKind.Message,
            Description = "A private message.",
        };
        dbContext.Interactions.AddRange(primaryInteraction, sharedInteraction, duplicateOnlyInteraction);
        dbContext.InteractionParticipants.AddRange(
            new InteractionParticipant { OwnerId = owner, InteractionId = primaryInteraction.Id, PersonId = primary },
            new InteractionParticipant { OwnerId = owner, InteractionId = sharedInteraction.Id, PersonId = primary },
            new InteractionParticipant { OwnerId = owner, InteractionId = sharedInteraction.Id, PersonId = duplicate },
            new InteractionParticipant { OwnerId = owner, InteractionId = duplicateOnlyInteraction.Id, PersonId = duplicate },
            new InteractionParticipant { OwnerId = owner, InteractionId = duplicateOnlyInteraction.Id, PersonId = other });
        await dbContext.SaveChangesAsync();

        return new Seeded(primary, duplicate, other, primaryEmail, repeatedEmail, phone, sharedInteraction.Id, duplicateOnlyInteraction.Id);
    }

    private sealed class CountingSaveInterceptor : SaveChangesInterceptor
    {
        public int Saves { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Saves++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static bool IsWrite(DbCommand command)
    {
        var text = command.CommandText;
        return text.Contains("INSERT INTO", StringComparison.Ordinal)
            || text.Contains("UPDATE [", StringComparison.Ordinal)
            || text.Contains("DELETE FROM", StringComparison.Ordinal);
    }

    private sealed class RecordingCommandInterceptor : DbCommandInterceptor
    {
        public List<(string Text, DbTransaction? Transaction)> Writes { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Record(DbCommand command)
        {
            if (IsWrite(command))
            {
                Writes.Add((command.CommandText, command.Transaction));
            }
        }
    }

    /// <summary>Lets every write through until the one that deletes the person, then throws.</summary>
    private sealed class FailOnPersonDeleteInterceptor : DbCommandInterceptor
    {
        public int WritesBeforeFailure { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Check(DbCommand command)
        {
            if (command.CommandText.Contains("DELETE FROM [People]", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("simulated failure");
            }

            if (IsWrite(command))
            {
                WritesBeforeFailure++;
            }
        }
    }

    /// <summary>Plays "deleted in another tab": just before the first save it deletes the person through a separate context.</summary>
    private sealed class DeletePersonOnFirstSaveInterceptor(SqlServerDatabaseFixture fixture, Guid personId) : SaveChangesInterceptor
    {
        private int _fired;

        public bool Fired => _fired > 0;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _fired) == 1)
            {
                await using var other = fixture.CreateDbContext();
                other.People.Remove(await other.People.SingleAsync(p => p.Id == personId, cancellationToken));
                await other.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }
}
