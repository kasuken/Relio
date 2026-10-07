using Microsoft.EntityFrameworkCore;
using Relio.Application.Interactions;
using Relio.Application.People;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Interactions;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Isolation;

[Collection(SqlServerCollection.Name)]
public sealed class ArchiveBoundarySqlIsolationTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task People_pages_hide_archived_rows_by_default_and_report_only_the_current_owners_counts()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        Guid activeA;
        Guid archivedA;
        Guid activeB;
        Guid archivedB;

        await using (var setup = fixture.CreateDbContext())
        {
            activeA = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerA.Id, "Active A");
            archivedA = await TestDataFactory.CreatePersonAsync(
                setup,
                harness.OwnerA.Id,
                "Archived A",
                isArchived: true);
            activeB = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerB.Id, "Active B");
            archivedB = await TestDataFactory.CreatePersonAsync(
                setup,
                harness.OwnerB.Id,
                "Archived B",
                isArchived: true);
        }

        await using (var ownerAScope = harness.AsOwnerA())
        {
            var peopleA = new PeopleService(ownerAScope.DbContext, ownerAScope.CurrentUser, harness.Clock);
            var activePage = await peopleA.ListPageAsync(new PeopleListQuery());
            activePage.People.TotalCount.Should().Be(1);
            activePage.People.Items.Select(person => person.Id).Should().ContainSingle().Which.Should().Be(activeA);
            activePage.People.Items.Select(person => person.Id).Should().NotContain(archivedA);
            activePage.People.Items.Select(person => person.Id).Should().NotContain(activeB);
            activePage.ActiveCount.Should().Be(1);
            activePage.ArchivedCount.Should().Be(1);

            var allPage = await peopleA.ListPageAsync(new PeopleListQuery { IncludeArchived = true });
            allPage.People.TotalCount.Should().Be(2);
            allPage.People.Items.Select(person => person.Id)
                .Should().BeEquivalentTo(new[] { activeA, archivedA });
        }

        await using var ownerBScope = harness.AsOwnerB();
        var peopleB = new PeopleService(ownerBScope.DbContext, ownerBScope.CurrentUser, harness.Clock);
        var activePageB = await peopleB.ListPageAsync(new PeopleListQuery());
        activePageB.People.TotalCount.Should().Be(1);
        activePageB.People.Items.Select(person => person.Id).Should().ContainSingle().Which.Should().Be(activeB);
        activePageB.ActiveCount.Should().Be(1);
        activePageB.ArchivedCount.Should().Be(1);
        var allPageB = await peopleB.ListPageAsync(new PeopleListQuery { IncludeArchived = true });
        allPageB.People.Items.Select(person => person.Id)
            .Should().BeEquivalentTo(new[] { activeB, archivedB });
    }

    [SqlServerFact]
    public async Task Interaction_participants_keep_only_the_current_profile_or_existing_archived_participants()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        var today = DateOnly.FromDateTime(harness.Clock.GetUtcNow().UtcDateTime);
        Guid activeA;
        Guid archivedPrimaryA;
        Guid archivedOtherA;
        Guid personB;

        await using (var setup = fixture.CreateDbContext())
        {
            activeA = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerA.Id, "Active A");
            archivedPrimaryA = await TestDataFactory.CreatePersonAsync(
                setup,
                harness.OwnerA.Id,
                "Archived primary A",
                isArchived: true);
            archivedOtherA = await TestDataFactory.CreatePersonAsync(
                setup,
                harness.OwnerA.Id,
                "Archived other A",
                isArchived: true);
            personB = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerB.Id, "Person B");
        }

        await using var ownerAScope = harness.AsOwnerA();
        var interactionsA = new InteractionService(
            ownerAScope.DbContext,
            ownerAScope.CurrentUser,
            harness.Clock);

        var activeCandidates = await interactionsA.ListParticipantCandidatesAsync(activeA);
        activeCandidates.Select(person => person.PersonId).Should().ContainSingle().Which.Should().Be(activeA);
        activeCandidates.Select(person => person.PersonId).Should().NotContain(personB);
        var foreignPrimaryCandidates = await interactionsA.ListParticipantCandidatesAsync(personB);
        var missingPrimaryCandidates = await interactionsA.ListParticipantCandidatesAsync(Guid.NewGuid());
        foreignPrimaryCandidates.Select(person => person.PersonId)
            .Should().BeEquivalentTo(new[] { activeA });
        missingPrimaryCandidates.Select(person => person.PersonId)
            .Should().BeEquivalentTo(foreignPrimaryCandidates.Select(person => person.PersonId));

        var archivedCandidates = await interactionsA.ListParticipantCandidatesAsync(archivedPrimaryA);
        archivedCandidates.Select(person => person.PersonId)
            .Should().BeEquivalentTo(new[] { activeA, archivedPrimaryA });
        archivedCandidates.Select(person => person.PersonId).Should().NotContain(archivedOtherA);
        archivedCandidates.Select(person => person.PersonId).Should().NotContain(personB);
        archivedCandidates.Single(person => person.PersonId == archivedPrimaryA).IsArchived.Should().BeTrue();

        var refusedCreate = () => interactionsA.CreateAsync(new CreateInteractionRequest
        {
            ProfilePersonId = activeA,
            OccurredOn = today,
            Kind = InteractionKind.Call,
            Description = "Rejected archived participant",
            ParticipantIds = [activeA, archivedOtherA],
        });
        var createError = await refusedCreate.Should().ThrowAsync<InteractionValidationException>();
        createError.Which.Errors.Should().ContainSingle()
            .Which.Should().Be(InteractionValidationError.ArchivedParticipantNotAllowed);

        var archivedProfileInteractionId = await interactionsA.CreateAsync(new CreateInteractionRequest
        {
            ProfilePersonId = archivedPrimaryA,
            OccurredOn = today,
            Kind = InteractionKind.Call,
            Description = "Archived profile interaction",
            ParticipantIds = [archivedPrimaryA],
        });
        var archivedProfileInteraction = await interactionsA.GetAsync(archivedProfileInteractionId);
        archivedProfileInteraction.Should().NotBeNull();
        archivedProfileInteraction!.Participants.Should().ContainSingle()
            .Which.IsArchived.Should().BeTrue();

        var refusedUpdate = () => interactionsA.UpdateAsync(
            archivedProfileInteractionId,
            new UpdateInteractionRequest
            {
                OccurredOn = today,
                Kind = InteractionKind.Meeting,
                Description = "Rejected newly added archived participant",
                ParticipantIds = [archivedPrimaryA, archivedOtherA],
            });
        var updateError = await refusedUpdate.Should().ThrowAsync<InteractionValidationException>();
        updateError.Which.Errors.Should().ContainSingle()
            .Which.Should().Be(InteractionValidationError.ArchivedParticipantNotAllowed);

        await using var verify = fixture.CreateDbContext();
        var ownerAInteractions = await verify.Interactions.AsNoTracking()
            .Where(interaction => interaction.OwnerId == harness.OwnerA.Id)
            .ToListAsync();
        ownerAInteractions.Should().ContainSingle().Which.Id.Should().Be(archivedProfileInteractionId);
        (await verify.InteractionParticipants.AsNoTracking()
            .Where(participant => participant.OwnerId == harness.OwnerA.Id
                && participant.InteractionId == archivedProfileInteractionId)
            .Select(participant => participant.PersonId)
            .ToListAsync())
            .Should().Equal(archivedPrimaryA);
        (await verify.Interactions.AsNoTracking().AnyAsync(interaction => interaction.OwnerId == harness.OwnerB.Id))
            .Should().BeFalse();
    }
}
