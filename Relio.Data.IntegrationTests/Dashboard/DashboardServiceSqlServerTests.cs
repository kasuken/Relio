using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Relio.Application.Dashboard;
using Relio.Application.Time;
using Relio.Data.Dashboard;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Dashboard;

[Collection(SqlServerCollection.Name)]
public sealed class DashboardServiceSqlServerTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task GetAsync_translates_bounded_sections_and_reads_shared_interactions_without_n_plus_one_queries()
    {
        var ownerId = TestDataFactory.NewOwnerId();
        var today = UserCalendar.Today(TimeProvider.System, TimeZoneInfo.Utc);
        var commandCounter = new ReaderCommandCounter();
        await using var dbContext = fixture.CreateDbContext(commandCounter);
        dbContext.UserProfiles.Add(new UserProfile { OwnerId = ownerId, TimeZoneId = "UTC" });

        var people = Enumerable.Range(0, 6)
            .Select(index => new Person
            {
                OwnerId = ownerId,
                FirstName = $"Person {index}",
                BirthdayMonth = today.AddDays(index + 1).Month,
                BirthdayDay = today.AddDays(index + 1).Day,
                StayInTouchCadenceDays = 30,
                LastContactedOn = today.AddDays(-40),
            })
            .ToArray();
        dbContext.People.AddRange(people);

        foreach (var person in people)
        {
            dbContext.Reminders.Add(new Reminder
            {
                OwnerId = ownerId,
                PersonId = person.Id,
                Title = "A reminder",
                DueDate = today.AddDays(1),
            });
            var interaction = new Interaction
            {
                OwnerId = ownerId,
                OccurredOn = today,
                Kind = InteractionKind.Call,
                Description = "A recent interaction",
            };
            dbContext.Interactions.Add(interaction);
            dbContext.InteractionParticipants.Add(new InteractionParticipant
            {
                OwnerId = ownerId,
                InteractionId = interaction.Id,
                PersonId = person.Id,
            });
        }

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        commandCounter.Commands.Clear();

        var snapshot = await new DashboardService(dbContext, new FakeCurrentUser(ownerId), TimeProvider.System)
            .GetAsync();

        snapshot.UpcomingReminders.Should().HaveCount(5);
        snapshot.UpcomingBirthdays.Should().HaveCount(5);
        snapshot.ReachOuts.Should().HaveCount(5);
        snapshot.RecentInteractions.Should().HaveCount(5);
        snapshot.RecentlyAddedPeople.Should().HaveCount(5);
        snapshot.RecentInteractions.Select(interaction => interaction.Id).Should().OnlyHaveUniqueItems();
        snapshot.RecentInteractions.Should().OnlyContain(interaction => interaction.Participants.Count == 1);
        commandCounter.Commands.Should().HaveCount(8, "the dashboard uses a fixed number of section queries, not a query per interaction");
        commandCounter.Commands.Should().OnlyContain(command => command.Contains("@", StringComparison.Ordinal));
        dbContext.ChangeTracker.Entries().Should().BeEmpty("the dashboard is read-only");
    }

    [SqlServerFact]
    public async Task GetAsync_scopes_each_section_and_related_rows_to_the_current_owner()
    {
        var ownerA = TestDataFactory.NewOwnerId();
        var ownerB = TestDataFactory.NewOwnerId();
        var today = UserCalendar.Today(TimeProvider.System, TimeZoneInfo.Utc);
        await using var dbContext = fixture.CreateDbContext();
        dbContext.UserProfiles.AddRange(
            new UserProfile { OwnerId = ownerA, TimeZoneId = "UTC" },
            new UserProfile { OwnerId = ownerB, TimeZoneId = "UTC" });

        var activeA = CreatePerson(ownerA, "Ada", today.AddDays(2), today.AddDays(-40));
        var archivedA = CreatePerson(ownerA, "Archived", today.AddDays(2), today.AddDays(-40), isArchived: true);
        var activeB = CreatePerson(ownerB, "Bea", today.AddDays(3), today.AddDays(-40));
        dbContext.People.AddRange(activeA, archivedA, activeB);

        var reminderA = CreateReminder(ownerA, activeA.Id, "A reminder", today.AddDays(2));
        var reminderB = CreateReminder(ownerB, activeB.Id, "B reminder", today.AddDays(3));
        var reminderForAnotherOwner = CreateReminder(ownerA, activeB.Id, "Wrong related owner", today.AddDays(1));
        var archivedReminder = CreateReminder(ownerA, archivedA.Id, "Archived reminder", today);
        dbContext.Reminders.AddRange(reminderA, reminderB, reminderForAnotherOwner, archivedReminder);

        var interactionA = CreateInteraction(ownerA, "A interaction", today);
        var archivedInteraction = CreateInteraction(ownerA, "Archived interaction", today.AddDays(-1));
        var interactionB = CreateInteraction(ownerB, "B interaction", today);
        dbContext.Interactions.AddRange(interactionA, archivedInteraction, interactionB);
        dbContext.InteractionParticipants.AddRange(
            CreateParticipant(ownerA, interactionA.Id, activeA.Id),
            CreateParticipant(ownerA, interactionA.Id, archivedA.Id),
            CreateParticipant(ownerA, interactionA.Id, activeB.Id),
            CreateParticipant(ownerA, archivedInteraction.Id, archivedA.Id),
            CreateParticipant(ownerB, interactionB.Id, activeB.Id));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var dashboardA = await new DashboardService(dbContext, new FakeCurrentUser(ownerA), TimeProvider.System)
            .GetAsync();
        var dashboardB = await new DashboardService(dbContext, new FakeCurrentUser(ownerB), TimeProvider.System)
            .GetAsync();

        dashboardA.ActivePeopleCount.Should().Be(1);
        dashboardA.ArchivedPeopleCount.Should().Be(1);
        dashboardA.UpcomingReminders.Select(reminder => reminder.Id).Should().Equal(reminderA.Id);
        dashboardA.UpcomingBirthdays.Select(birthday => birthday.PersonId).Should().Equal(activeA.Id);
        dashboardA.ReachOuts.Select(reachOut => reachOut.PersonId).Should().Equal(activeA.Id);
        dashboardA.RecentInteractions.Select(interaction => interaction.Id).Should().Equal(interactionA.Id);
        dashboardA.RecentInteractions[0].Participants.Select(participant => participant.PersonId)
            .Should().Equal(activeA.Id);
        dashboardA.RecentlyAddedPeople.Select(person => person.PersonId).Should().Equal(activeA.Id);

        dashboardB.ActivePeopleCount.Should().Be(1);
        dashboardB.ArchivedPeopleCount.Should().Be(0);
        dashboardB.UpcomingReminders.Select(reminder => reminder.Id).Should().Equal(reminderB.Id);
        dashboardB.UpcomingBirthdays.Select(birthday => birthday.PersonId).Should().Equal(activeB.Id);
        dashboardB.ReachOuts.Select(reachOut => reachOut.PersonId).Should().Equal(activeB.Id);
        dashboardB.RecentInteractions.Select(interaction => interaction.Id).Should().Equal(interactionB.Id);
        dashboardB.RecentInteractions[0].Participants.Select(participant => participant.PersonId)
            .Should().Equal(activeB.Id);
        dashboardB.RecentlyAddedPeople.Select(person => person.PersonId).Should().Equal(activeB.Id);
        dbContext.ChangeTracker.Entries().Should().BeEmpty("cross-owner dashboard reads are untracked");
    }

    private static Person CreatePerson(
        string ownerId,
        string firstName,
        DateOnly birthday,
        DateOnly lastContactedOn,
        bool isArchived = false) =>
        new()
        {
            OwnerId = ownerId,
            FirstName = firstName,
            BirthdayMonth = birthday.Month,
            BirthdayDay = birthday.Day,
            LastContactedOn = lastContactedOn,
            StayInTouchCadenceDays = 30,
            IsArchived = isArchived,
            ArchivedAtUtc = isArchived ? TimeProvider.System.GetUtcNow().UtcDateTime : null,
        };

    private static Reminder CreateReminder(string ownerId, Guid personId, string title, DateOnly dueDate) =>
        new()
        {
            OwnerId = ownerId,
            PersonId = personId,
            Title = title,
            DueDate = dueDate,
        };

    private static Interaction CreateInteraction(string ownerId, string description, DateOnly occurredOn) =>
        new()
        {
            OwnerId = ownerId,
            Description = description,
            OccurredOn = occurredOn,
            Kind = InteractionKind.Call,
        };

    private static InteractionParticipant CreateParticipant(string ownerId, Guid interactionId, Guid personId) =>
        new()
        {
            OwnerId = ownerId,
            InteractionId = interactionId,
            PersonId = personId,
        };

    private sealed class ReaderCommandCounter : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
