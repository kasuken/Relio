using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Relio.Application.Interactions;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Interactions;

/// <summary>What a real SQL Server proves for multi-person interactions and their person dates.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class InteractionServiceSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Create_update_and_delete_recalculate_each_participants_last_contact_date()
    {
        var ownerId = await TestDataFactory.CreateOwnerAsync(fixture);
        var (firstPersonId, secondPersonId, thirdPersonId) = await SeedPeopleAsync(ownerId);
        var today = await TodayAsync(ownerId);
        var olderDate = today.AddDays(-3);
        var newerDate = today.AddDays(-1);
        await using var dbContext = fixture.CreateDbContext();
        var service = TestDataFactory.CreateInteractionService(dbContext, ownerId);

        var olderId = await service.CreateAsync(new CreateInteractionRequest
        {
            ProfilePersonId = firstPersonId,
            OccurredOn = olderDate,
            Kind = InteractionKind.Message,
            Description = "A shared message",
            ParticipantIds = [firstPersonId, secondPersonId],
        });
        var newerId = await service.CreateAsync(new CreateInteractionRequest
        {
            ProfilePersonId = firstPersonId,
            OccurredOn = newerDate,
            Kind = InteractionKind.Call,
            Description = "A later call",
            ParticipantIds = [firstPersonId],
        });

        dbContext.ChangeTracker.Entries().Should().BeEmpty("every mutation clears the circuit-scoped tracker");
        await using (var verify = fixture.CreateDbContext())
        {
            var dates = await verify.People.AsNoTracking()
                .Where(person => person.Id == firstPersonId || person.Id == secondPersonId)
                .ToDictionaryAsync(person => person.Id, person => person.LastContactedOn);
            dates[firstPersonId].Should().Be(newerDate);
            dates[secondPersonId].Should().Be(olderDate);
        }

        (await service.UpdateAsync(newerId, new UpdateInteractionRequest
        {
            OccurredOn = today.AddDays(-2),
            Kind = InteractionKind.Meeting,
            Description = "Changed to a meeting",
            ParticipantIds = [secondPersonId, thirdPersonId],
        })).Should().BeTrue();

        await using (var verify = fixture.CreateDbContext())
        {
            var dates = await verify.People.AsNoTracking()
                .Where(person => person.Id == firstPersonId
                    || person.Id == secondPersonId
                    || person.Id == thirdPersonId)
                .ToDictionaryAsync(person => person.Id, person => person.LastContactedOn);
            dates[firstPersonId].Should().Be(olderDate, "removing the newer interaction restores the remaining maximum");
            dates[secondPersonId].Should().Be(today.AddDays(-2));
            dates[thirdPersonId].Should().Be(today.AddDays(-2));
        }

        (await service.DeleteAsync(newerId)).Should().BeTrue();
        await using (var verify = fixture.CreateDbContext())
        {
            var dates = await verify.People.AsNoTracking()
                .Where(person => person.Id == firstPersonId
                    || person.Id == secondPersonId
                    || person.Id == thirdPersonId)
                .ToDictionaryAsync(person => person.Id, person => person.LastContactedOn);
            dates[firstPersonId].Should().Be(olderDate);
            dates[secondPersonId].Should().Be(olderDate);
            dates[thirdPersonId].Should().BeNull("a participant with no surviving interactions returns to never contacted");
        }

        (await service.DeleteAsync(olderId)).Should().BeTrue();
        await using var final = fixture.CreateDbContext();
        (await final.People.AsNoTracking()
            .Where(person => person.Id == firstPersonId || person.Id == secondPersonId)
            .AllAsync(person => person.LastContactedOn == null))
            .Should().BeTrue();
        (await final.Interactions.CountAsync(interaction => interaction.OwnerId == ownerId)).Should().Be(0);
        (await final.InteractionParticipants.CountAsync(participant => participant.OwnerId == ownerId)).Should().Be(0);
    }

    [SqlServerFact]
    public async Task Foreign_participant_ids_and_foreign_interactions_are_indistinguishable_and_change_nothing()
    {
        var ownerA = await TestDataFactory.CreateOwnerAsync(fixture);
        var ownerB = await TestDataFactory.CreateOwnerAsync(fixture);
        var (personA, secondPersonA, thirdPersonA) = await SeedPeopleAsync(ownerA);
        var (personB, _, _) = await SeedPeopleAsync(ownerB);
        var todayA = await TodayAsync(ownerA);
        var todayB = await TodayAsync(ownerB);
        var foreignInteractionId = await CreateInteractionAsync(ownerB, personB, todayB);
        await using var dbContext = fixture.CreateDbContext();
        var serviceA = TestDataFactory.CreateInteractionService(dbContext, ownerA);

        var act = () => serviceA.CreateAsync(new CreateInteractionRequest
        {
            ProfilePersonId = personA,
            OccurredOn = todayA,
            Kind = InteractionKind.Call,
            Description = "A private detail",
            ParticipantIds = [personA, personB],
        });
        await act.Should().ThrowAsync<ForeignEntityNotOwnedException>();

        (await serviceA.GetAsync(foreignInteractionId)).Should().BeNull();
        (await serviceA.UpdateAsync(foreignInteractionId, new UpdateInteractionRequest
        {
            OccurredOn = todayA,
            Kind = InteractionKind.Call,
            Description = "A private detail",
            ParticipantIds = [personA],
        })).Should().BeFalse();
        (await serviceA.DeleteAsync(foreignInteractionId)).Should().BeFalse();
        var candidateIds = (await serviceA.ListParticipantCandidatesAsync(personA))
            .Select(person => person.PersonId)
            .ToArray();
        candidateIds.Should().BeEquivalentTo(new[] { personA, secondPersonA, thirdPersonA });
        candidateIds.Should().NotContain(personB);

        await using var verify = fixture.CreateDbContext();
        (await verify.Interactions.CountAsync(interaction => interaction.OwnerId == ownerA)).Should().Be(0);
        (await verify.InteractionParticipants.CountAsync(participant => participant.OwnerId == ownerA)).Should().Be(0);
        (await verify.People.AsNoTracking().Where(person => person.Id == personA)
            .Select(person => person.LastContactedOn).SingleAsync()).Should().BeNull();
        (await verify.Interactions.CountAsync(interaction => interaction.OwnerId == ownerB)).Should().Be(1);
    }

    [SqlServerFact]
    public async Task Interaction_indexes_and_participant_constraints_are_present_in_the_migrated_database()
    {
        var ownerId = await TestDataFactory.CreateOwnerAsync(fixture);
        var (personId, _, _) = await SeedPeopleAsync(ownerId);
        var interactionId = await CreateInteractionAsync(ownerId, personId, await TodayAsync(ownerId));
        await using var dbContext = fixture.CreateDbContext();

        var interactionIndexes = await dbContext.Database.SqlQuery<string>($"""
            SELECT CONVERT(nvarchar(300), i.name) COLLATE DATABASE_DEFAULT AS [Value]
            FROM sys.indexes i
            WHERE i.object_id = OBJECT_ID(N'dbo.Interactions') AND i.name IS NOT NULL
            """).ToListAsync();
        interactionIndexes.Should().Contain(
            "IX_Interactions_OwnerId_OccurredOn_Id",
            "interaction browsing is scoped by owner and calendar date");
        interactionIndexes.Should().Contain(
            "IX_Interactions_OwnerId_OccurredOn_CreatedAtUtc_Id",
            "the timeline reads a bounded, deterministic date/timestamp/id stream");

        var participantIndexes = await dbContext.Database.SqlQuery<string>($"""
            SELECT CONVERT(nvarchar(300), i.name) COLLATE DATABASE_DEFAULT AS [Value]
            FROM sys.indexes i
            WHERE i.object_id = OBJECT_ID(N'dbo.InteractionParticipants') AND i.name IS NOT NULL
            """).ToListAsync();
        participantIndexes.Should().Contain("IX_InteractionParticipants_InteractionId_PersonId");
        participantIndexes.Should().Contain("IX_InteractionParticipants_OwnerId_PersonId_InteractionId");

        var uniqueParticipantIndex = dbContext.Model.FindEntityType(typeof(InteractionParticipant))!
            .GetIndexes()
            .Single(index => index.IsUnique);
        uniqueParticipantIndex.Properties.Select(property => property.Name)
            .Should().Equal(nameof(InteractionParticipant.InteractionId), nameof(InteractionParticipant.PersonId));

        var interaction = await dbContext.Interactions.SingleAsync(item => item.Id == interactionId);
        dbContext.InteractionParticipants.Add(new InteractionParticipant
        {
            OwnerId = ownerId,
            InteractionId = interaction.Id,
            PersonId = personId,
        });
        var duplicateLink = async () => await dbContext.SaveChangesAsync();
        var duplicateException = await duplicateLink.Should().ThrowAsync<DbUpdateException>();
        FindSqlNumber(duplicateException.Which).Should().BeOneOf(2601, 2627);

        await using var invalidKindContext = fixture.CreateDbContext();
        invalidKindContext.Interactions.Add(new Interaction
        {
            OwnerId = ownerId,
            OccurredOn = await TodayAsync(ownerId),
            Kind = (InteractionKind)99,
            Description = "Invalid kind",
        });
        var invalidKind = async () => await invalidKindContext.SaveChangesAsync();
        var checkException = await invalidKind.Should().ThrowAsync<DbUpdateException>();
        FindSqlNumber(checkException.Which).Should().Be(547, "the CK_Interactions_Kind check constraint is the database backstop");
    }

    private async Task<(Guid First, Guid Second, Guid Third)> SeedPeopleAsync(string ownerId)
    {
        await using var dbContext = fixture.CreateDbContext();
        var people = TestDataFactory.CreateService(dbContext, ownerId);
        var first = (await people.CreateAsync(new CreatePersonRequest { FirstName = "Ada" })).Id;
        var second = (await people.CreateAsync(new CreatePersonRequest { FirstName = "Bea" })).Id;
        var third = (await people.CreateAsync(new CreatePersonRequest { FirstName = "Cleo" })).Id;
        return (first, second, third);
    }

    private async Task<DateOnly> TodayAsync(string ownerId)
    {
        await using var dbContext = fixture.CreateDbContext();
        var profile = await dbContext.UserProfiles.SingleOrDefaultAsync(candidate => candidate.OwnerId == ownerId);
        if (profile is null)
        {
            dbContext.UserProfiles.Add(new UserProfile
            {
                OwnerId = ownerId,
                TimeZoneId = "Pacific/Kiritimati",
            });
            await dbContext.SaveChangesAsync();
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati");
        return Relio.Application.Time.UserCalendar.Today(TimeProvider.System, timeZone);
    }

    private async Task<Guid> CreateInteractionAsync(string ownerId, Guid personId, DateOnly date)
    {
        await using var dbContext = fixture.CreateDbContext();
        return await TestDataFactory.CreateInteractionService(dbContext, ownerId).CreateAsync(
            new CreateInteractionRequest
            {
                ProfilePersonId = personId,
                OccurredOn = date,
                Kind = InteractionKind.Call,
                Description = "A SQL Server integration interaction",
                ParticipantIds = [personId],
            });
    }

    private static int? FindSqlNumber(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException)
            {
                return sqlException.Number;
            }
        }

        return null;
    }
}
