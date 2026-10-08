using Microsoft.EntityFrameworkCore;
using Relio.Application.DifficultMoments;
using Relio.Application.Ownership;
using Relio.Data.Encryption;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.DifficultMoments;

/// <summary>
/// SQL Server-only guarantees for difficult moments: indexes, cascade on person delete,
/// field protection round trip, cross-owner isolation, foreign id rejection, and archive behavior.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class DifficultMomentSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerTheory]
    [InlineData("IX_DifficultMoments_OwnerId_PersonId_OccurredOn", "OwnerId,PersonId,OccurredOn")]
    [InlineData("IX_DifficultMoments_OwnerId_Status_OccurredOn", "OwnerId,Status,OccurredOn")]
    [InlineData("IX_DifficultMoments_OwnerId_RecurrenceOfId", "OwnerId,RecurrenceOfId")]
    public async Task Difficult_moment_indexes_exist_with_the_requested_columns_in_order(string indexName, string expectedColumns)
    {
        await using var dbContext = fixture.CreateDbContext();

        var columns = await dbContext.Database.SqlQuery<string>($"""
            SELECT c.name AS [Value]
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID('dbo.DifficultMoments') AND i.name = {indexName} AND ic.is_included_column = 0
            ORDER BY ic.key_ordinal
            """).ToListAsync();

        string.Join(",", columns).Should().Be(expectedColumns);
    }

    [SqlServerFact]
    public async Task Difficult_moment_creation_and_reads_round_trip_with_sql_server()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid personId;
        Guid momentId;
        await using (var setup = fixture.CreateDbContext())
        {
            personId = await TestDataFactory.CreatePersonAsync(setup, owner, "Ada");
            var created = await TestDataFactory.CreateDifficultMomentService(setup, owner).CreateAsync(
                new CreateDifficultMomentRequest
                {
                    PersonId = personId,
                    OccurredOn = new DateOnly(2026, 5, 1),
                    Description = "A challenging disagreement on project scope.",
                    Trigger = "Sudden schedule pressure.",
                    Resolution = "Had an open 1:1 conversation and renegotiated milestones.",
                    LessonsLearned = "Check expectations early.",
                    Status = DifficultMomentStatus.Resolved,
                    ResolvedOn = new DateOnly(2026, 5, 2),
                });
            momentId = created.Id;
            created.Description.Should().Be("A challenging disagreement on project scope.");
            created.Trigger.Should().Be("Sudden schedule pressure.");
            created.Resolution.Should().Be("Had an open 1:1 conversation and renegotiated milestones.");
            created.LessonsLearned.Should().Be("Check expectations early.");
            created.Status.Should().Be(DifficultMomentStatus.Resolved);
        }

        await using var fresh = fixture.CreateDbContext();
        var service = TestDataFactory.CreateDifficultMomentService(fresh, owner);
        var moment = await service.GetAsync(momentId);
        moment.Should().NotBeNull();
        moment!.Description.Should().Be("A challenging disagreement on project scope.");

        var stored = await fresh.Database.SqlQuery<string>(
            $"SELECT [Description] AS [Value] FROM dbo.DifficultMoments WHERE [Id] = {momentId}").SingleAsync();
        stored.Should().NotContain("challenging disagreement");
        FieldProtector.Unprotect(stored, ProtectedFieldPurposes.DifficultMomentDescription).Should().Be(moment.Description);
    }

    [SqlServerFact]
    public async Task Deleting_a_person_directly_in_sql_server_cascades_to_difficult_moments()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid personId;
        Guid momentId;
        await using (var setup = fixture.CreateDbContext())
        {
            personId = await TestDataFactory.CreatePersonAsync(setup, owner, "Ada");
            var created = await TestDataFactory.CreateDifficultMomentService(setup, owner).CreateAsync(
                new CreateDifficultMomentRequest
                {
                    PersonId = personId,
                    OccurredOn = new DateOnly(2026, 5, 1),
                    Description = "Cascade test moment.",
                });
            momentId = created.Id;
        }

        await using (var delete = fixture.CreateDbContext())
        {
            (await delete.People.Where(person => person.Id == personId && person.OwnerId == owner)
                .ExecuteDeleteAsync()).Should().Be(1);
        }

        await using var verify = fixture.CreateDbContext();
        (await verify.Set<DifficultMoment>().AsNoTracking().CountAsync(m => m.Id == momentId)).Should().Be(0);
    }

    [SqlServerFact]
    public async Task Difficult_moments_are_isolated_by_owner_for_all_reads_and_mutations()
    {
        var ownerA = await TestDataFactory.CreateOwnerAsync(fixture);
        var ownerB = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid personA;
        Guid personB;
        Guid momentA;

        await using (var setup = fixture.CreateDbContext())
        {
            personA = await TestDataFactory.CreatePersonAsync(setup, ownerA, "Ada");
            personB = await TestDataFactory.CreatePersonAsync(setup, ownerB, "Bob");
            var created = await TestDataFactory.CreateDifficultMomentService(setup, ownerA).CreateAsync(
                new CreateDifficultMomentRequest
                {
                    PersonId = personA,
                    OccurredOn = new DateOnly(2026, 5, 1),
                    Description = "Private moment A.",
                    Status = DifficultMomentStatus.Open,
                });
            momentA = created.Id;
        }

        await using (var db = fixture.CreateDbContext())
        {
            var serviceB = TestDataFactory.CreateDifficultMomentService(db, ownerB);

            (await serviceB.GetAsync(momentA)).Should().BeNull();
            (await serviceB.ListForPersonAsync(personA)).Should().BeEmpty();
            (await serviceB.ListOverviewAsync(new DifficultMomentFilterRequest())).Should().BeEmpty();
            (await serviceB.ListCandidatesForRecurrenceAsync(personA)).Should().BeEmpty();

            var updateAct = () => serviceB.UpdateAsync(momentA, new UpdateDifficultMomentRequest
            {
                OccurredOn = new DateOnly(2026, 5, 1),
                Description = "Tampered description",
                Status = DifficultMomentStatus.Open,
            });
            (await updateAct()).Should().BeFalse();

            (await serviceB.DeleteAsync(momentA)).Should().BeFalse();

            var foreignPersonAct = () => serviceB.CreateAsync(new CreateDifficultMomentRequest
            {
                PersonId = personA,
                OccurredOn = new DateOnly(2026, 5, 1),
                Description = "Attempt with foreign person",
            });
            await foreignPersonAct.Should().ThrowAsync<ForeignEntityNotOwnedException>();

            var foreignRecurrenceAct = () => serviceB.CreateAsync(new CreateDifficultMomentRequest
            {
                PersonId = personB,
                OccurredOn = new DateOnly(2026, 5, 1),
                Description = "Attempt with foreign recurrence",
                RecurrenceOfId = momentA,
            });
            await foreignRecurrenceAct.Should().ThrowAsync<ForeignEntityNotOwnedException>();
        }
    }

    [SqlServerFact]
    public async Task Overview_list_excludes_archived_people_by_default()
    {
        var owner = await TestDataFactory.CreateOwnerAsync(fixture);
        Guid activePerson;
        Guid archivedPerson;

        await using (var setup = fixture.CreateDbContext())
        {
            activePerson = await TestDataFactory.CreatePersonAsync(setup, owner, "Active Person");
            archivedPerson = await TestDataFactory.CreatePersonAsync(setup, owner, "Archived Person", isArchived: true);
            var setupService = TestDataFactory.CreateDifficultMomentService(setup, owner);
            await setupService.CreateAsync(new CreateDifficultMomentRequest
            {
                PersonId = activePerson,
                OccurredOn = new DateOnly(2026, 5, 1),
                Description = "Moment on active person.",
            });
            await setupService.CreateAsync(new CreateDifficultMomentRequest
            {
                PersonId = archivedPerson,
                OccurredOn = new DateOnly(2026, 5, 2),
                Description = "Moment on archived person.",
            });
        }

        await using var db = fixture.CreateDbContext();
        var service = TestDataFactory.CreateDifficultMomentService(db, owner);

        var defaultOverview = await service.ListOverviewAsync(new DifficultMomentFilterRequest());
        defaultOverview.Should().ContainSingle().Which.PersonId.Should().Be(activePerson);

        var withArchived = await service.ListOverviewAsync(new DifficultMomentFilterRequest { IncludeArchived = true });
        withArchived.Should().HaveCount(2);
    }
}
