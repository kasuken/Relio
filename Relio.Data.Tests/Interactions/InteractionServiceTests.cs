using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Interactions;
using Relio.Application.Ownership;
using Relio.Application.Security;
using Relio.Data.Interactions;
using Relio.Data.Tests.People;
using Relio.Domain;

namespace Relio.Data.Tests.Interactions;

public class InteractionServiceTests
{
    private const string Owner = "interaction-owner";
    private const string OtherOwner = "other-owner";

    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_get_and_picker_are_owner_scoped_and_include_all_participants()
    {
        await using var dbContext = CreateDbContext();
        var people = await SeedPeopleAsync(dbContext);
        dbContext.People.Single(person => person.Id == people.ArchivedId).IsArchived = true;
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var service = CreateService(dbContext);

        var interactionId = await service.CreateAsync(Request(people.FirstId, [people.FirstId, people.SecondId]));

        var interaction = await service.GetAsync(interactionId);
        interaction.Should().NotBeNull();
        interaction!.Participants.Select(person => person.PersonId)
            .Should().BeEquivalentTo(new[] { people.FirstId, people.SecondId });
        interaction.Description.Should().Be("Talked about the weekend.");
        var candidates = await service.ListParticipantCandidatesAsync(people.FirstId);
        var candidateIds = candidates.Select(person => person.PersonId);
        candidateIds.Should().Contain(people.FirstId);
        candidateIds.Should().Contain(people.SecondId);
        candidateIds.Should().NotContain(people.ArchivedId);
        candidateIds.Should().NotContain(people.ForeignId);
    }

    [Fact]
    public async Task Picker_keeps_the_current_archived_profile_and_existing_archived_participants()
    {
        await using var dbContext = CreateDbContext();
        var people = await SeedPeopleAsync(dbContext);
        dbContext.People.Single(person => person.Id == people.ArchivedId).IsArchived = true;
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var candidates = await CreateService(dbContext).ListParticipantCandidatesAsync(
            people.ArchivedId,
            [people.ArchivedId]);

        candidates.Select(person => person.PersonId).Should().Contain(people.ArchivedId);
        candidates.Single(person => person.PersonId == people.ArchivedId).IsArchived.Should().BeTrue();
        candidates.Select(person => person.PersonId).Should().NotContain(people.ForeignId);
    }

    [Fact]
    public async Task Get_update_and_delete_for_another_users_interaction_are_indistinguishable()
    {
        await using var dbContext = CreateDbContext();
        var people = await SeedPeopleAsync(dbContext);
        var interactionId = await CreateService(dbContext).CreateAsync(Request(people.FirstId, [people.FirstId]));
        dbContext.ChangeTracker.Clear();
        var serviceForOtherOwner = CreateService(dbContext, OtherOwner);

        (await serviceForOtherOwner.GetAsync(interactionId)).Should().BeNull();
        (await serviceForOtherOwner.UpdateAsync(interactionId, UpdateRequest([people.ForeignId]))).Should().BeFalse();
        (await serviceForOtherOwner.DeleteAsync(interactionId)).Should().BeFalse();

        (await dbContext.Interactions.CountAsync()).Should().Be(1);
        (await dbContext.InteractionParticipants.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Creating_with_another_users_person_fails_before_saving_anything()
    {
        var saves = new CountingSaveChangesInterceptor();
        await using var dbContext = CreateDbContext(saves);
        var people = await SeedPeopleAsync(dbContext);
        saves.Saves = 0;
        dbContext.ChangeTracker.Clear();
        var service = CreateService(dbContext, OtherOwner);

        var exception = await FluentActions.Awaiting(() => service.CreateAsync(Request(people.ForeignId, [people.ForeignId, people.FirstId])))
            .Should().ThrowAsync<ForeignEntityNotOwnedException>();

        exception.Which.EntityName.Should().Be(ForeignEntityNames.People);
        saves.Saves.Should().Be(0);
        (await dbContext.Interactions.CountAsync()).Should().Be(0);
        (await dbContext.InteractionParticipants.CountAsync()).Should().Be(0);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task Updating_with_another_users_person_fails_before_mutating_the_interaction()
    {
        await using var dbContext = CreateDbContext();
        var people = await SeedPeopleAsync(dbContext);
        var interactionId = await CreateService(dbContext).CreateAsync(Request(people.FirstId, [people.FirstId]));
        dbContext.ChangeTracker.Clear();

        var exception = await FluentActions.Awaiting(() => CreateService(dbContext).UpdateAsync(
                interactionId,
                UpdateRequest([people.FirstId, people.ForeignId])))
            .Should().ThrowAsync<ForeignEntityNotOwnedException>();

        exception.Which.EntityName.Should().Be(ForeignEntityNames.People);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
        var interaction = await dbContext.Interactions.AsNoTracking().SingleAsync();
        interaction.Description.Should().Be("Talked about the weekend.");
        (await dbContext.InteractionParticipants.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Create_uses_the_users_calendar_day_across_UTC_boundaries_and_DST()
    {
        await VerifyUserDayAsync(
            "Pacific/Kiritimati",
            new DateTimeOffset(2026, 10, 6, 0, 30, 0, TimeSpan.Zero),
            new DateOnly(2026, 10, 6));
        await VerifyUserDayAsync(
            "Pacific/Pago_Pago",
            new DateTimeOffset(2026, 10, 6, 0, 30, 0, TimeSpan.Zero),
            new DateOnly(2026, 10, 5));
        await VerifyUserDayAsync(
            "America/New_York",
            new DateTimeOffset(2026, 3, 8, 6, 30, 0, TimeSpan.Zero),
            new DateOnly(2026, 3, 8));
    }

    [Fact]
    public async Task Create_rejects_a_date_after_today_in_the_users_calendar()
    {
        var now = new DateTimeOffset(2026, 10, 6, 0, 30, 0, TimeSpan.Zero);
        await using var dbContext = CreateDbContext(now: now);
        var person = new Person { OwnerId = Owner, FirstName = "Kai" };
        dbContext.People.Add(person);
        dbContext.UserProfiles.Add(new UserProfile { OwnerId = Owner, TimeZoneId = "Pacific/Kiritimati" });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var exception = await FluentActions.Awaiting(() => CreateService(dbContext, now: now).CreateAsync(
                Request(person.Id, [person.Id], occurredOn: new DateOnly(2026, 10, 7))))
            .Should().ThrowAsync<InteractionValidationException>();

        exception.Which.Errors.Should().Contain(InteractionValidationError.DateInFuture);
        exception.Which.Message.Should().NotContain("2026");
        (await dbContext.Interactions.CountAsync()).Should().Be(0);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task Update_recalculates_last_contacted_after_date_and_participant_changes()
    {
        await using var dbContext = CreateDbContext();
        var people = await SeedPeopleAsync(dbContext);
        var service = CreateService(dbContext);
        var olderInteractionId = await service.CreateAsync(Request(
            people.FirstId,
            [people.FirstId, people.SecondId],
            occurredOn: new DateOnly(2026, 10, 1)));
        await service.CreateAsync(Request(
            people.FirstId,
            [people.FirstId],
            occurredOn: new DateOnly(2026, 10, 3)));
        dbContext.ChangeTracker.Clear();

        (await service.UpdateAsync(
            olderInteractionId,
            UpdateRequest(
                [people.ThirdId],
                occurredOn: new DateOnly(2026, 9, 30))))
            .Should().BeTrue();

        (await dbContext.People.AsNoTracking().SingleAsync(person => person.Id == people.FirstId))
            .LastContactedOn.Should().Be(new DateOnly(2026, 10, 3), "the newer remaining interaction still exists");
        (await dbContext.People.AsNoTracking().SingleAsync(person => person.Id == people.SecondId))
            .LastContactedOn.Should().BeNull("the moved interaction was the only one they shared");
        (await dbContext.People.AsNoTracking().SingleAsync(person => person.Id == people.ThirdId))
            .LastContactedOn.Should().Be(new DateOnly(2026, 9, 30), "the new participant receives the edited date");
        (await dbContext.InteractionParticipants.Where(participant => participant.InteractionId == olderInteractionId)
            .Select(participant => participant.PersonId)
            .ToListAsync()).Should().BeEquivalentTo(new[] { people.ThirdId });
    }

    [Fact]
    public async Task Deleting_interactions_recalculates_last_contacted_until_it_is_null()
    {
        await using var dbContext = CreateDbContext();
        var people = await SeedPeopleAsync(dbContext);
        var service = CreateService(dbContext);
        var olderId = await service.CreateAsync(Request(people.FirstId, [people.FirstId], new DateOnly(2026, 10, 1)));
        var newerId = await service.CreateAsync(Request(people.FirstId, [people.FirstId], new DateOnly(2026, 10, 4)));

        (await service.DeleteAsync(newerId)).Should().BeTrue();
        (await dbContext.People.AsNoTracking().SingleAsync(person => person.Id == people.FirstId))
            .LastContactedOn.Should().Be(new DateOnly(2026, 10, 1));

        (await service.DeleteAsync(olderId)).Should().BeTrue();
        (await dbContext.People.AsNoTracking().SingleAsync(person => person.Id == people.FirstId))
            .LastContactedOn.Should().BeNull();
    }

    [Fact]
    public async Task One_mutation_saves_once_and_clears_the_tracker()
    {
        var saves = new CountingSaveChangesInterceptor();
        await using var dbContext = CreateDbContext(saves);
        var people = await SeedPeopleAsync(dbContext);
        dbContext.ChangeTracker.Clear();
        saves.Saves = 0;

        await CreateService(dbContext).CreateAsync(Request(people.FirstId, [people.FirstId, people.SecondId]));

        saves.Saves.Should().Be(1);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_save_leaves_nothing_tracked_or_reinserted_on_the_next_call()
    {
        var failure = new FailOnceSaveChangesInterceptor();
        await using var dbContext = CreateDbContext(failure);
        var people = await SeedPeopleAsync(dbContext);
        dbContext.ChangeTracker.Clear();
        failure.Armed = true;
        var service = CreateService(dbContext);

        await FluentActions.Awaiting(() => service.CreateAsync(Request(people.FirstId, [people.FirstId])))
            .Should().ThrowAsync<InvalidOperationException>();

        dbContext.ChangeTracker.Entries().Should().BeEmpty();
        var createdId = await service.CreateAsync(Request(people.FirstId, [people.FirstId]));
        (await dbContext.Interactions.CountAsync()).Should().Be(1);
        (await dbContext.Interactions.AnyAsync(interaction => interaction.Id == createdId)).Should().BeTrue();
        (await dbContext.InteractionParticipants.CountAsync()).Should().Be(1);
    }

    private static async Task VerifyUserDayAsync(string timeZoneId, DateTimeOffset now, DateOnly occurredOn)
    {
        await using var dbContext = CreateDbContext(now: now);
        var person = new Person { OwnerId = Owner, FirstName = "Mika" };
        dbContext.People.Add(person);
        dbContext.UserProfiles.Add(new UserProfile { OwnerId = Owner, TimeZoneId = timeZoneId });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var interactionId = await CreateService(dbContext, now: now).CreateAsync(Request(person.Id, [person.Id], occurredOn));

        (await dbContext.Interactions.AsNoTracking().SingleAsync(interaction => interaction.Id == interactionId))
            .OccurredOn.Should().Be(occurredOn);
    }

    private static CreateInteractionRequest Request(
        Guid profilePersonId,
        IReadOnlyList<Guid> participantIds,
        DateOnly? occurredOn = null) =>
        new()
        {
            ProfilePersonId = profilePersonId,
            OccurredOn = occurredOn ?? new DateOnly(2026, 10, 6),
            Kind = InteractionKind.Call,
            Description = "  Talked about the weekend.  ",
            ParticipantIds = participantIds,
        };

    private static UpdateInteractionRequest UpdateRequest(
        IReadOnlyList<Guid> participantIds,
        DateOnly? occurredOn = null) =>
        new()
        {
            OccurredOn = occurredOn ?? new DateOnly(2026, 10, 6),
            Kind = InteractionKind.Meeting,
            Description = "  Met for coffee.  ",
            ParticipantIds = participantIds,
        };

    private static async Task<SeededPeople> SeedPeopleAsync(RelioDbContext dbContext)
    {
        var first = new Person { OwnerId = Owner, FirstName = "Ari" };
        var second = new Person { OwnerId = Owner, FirstName = "Bo" };
        var third = new Person { OwnerId = Owner, FirstName = "Cy" };
        var archived = new Person { OwnerId = Owner, FirstName = "Dee", IsArchived = true };
        var foreign = new Person { OwnerId = OtherOwner, FirstName = "Eli" };
        dbContext.People.AddRange(first, second, third, archived, foreign);
        dbContext.UserProfiles.AddRange(
            new UserProfile { OwnerId = Owner, TimeZoneId = "UTC" },
            new UserProfile { OwnerId = OtherOwner, TimeZoneId = "UTC" });
        await dbContext.SaveChangesAsync();
        return new SeededPeople(first.Id, second.Id, third.Id, archived.Id, foreign.Id);
    }

    private static RelioDbContext CreateDbContext(
        SaveChangesInterceptor? interceptor = null,
        DateTimeOffset? now = null)
    {
        var builder = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new RelioDbContext(builder.Options, new FakeTimeProvider(now ?? Now));
    }

    private static InteractionService CreateService(RelioDbContext dbContext, string ownerId = Owner, DateTimeOffset? now = null) =>
        new(dbContext, new FakeCurrentUser(ownerId), new FakeTimeProvider(now ?? Now));

    private sealed record SeededPeople(Guid FirstId, Guid SecondId, Guid ThirdId, Guid ArchivedId, Guid ForeignId);

    private sealed class CountingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public int Saves { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Saves++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FailOnceSaveChangesInterceptor : SaveChangesInterceptor
    {
        private bool _hasFailed;

        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && !_hasFailed)
            {
                _hasFailed = true;
                throw new InvalidOperationException("simulated save failure");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
