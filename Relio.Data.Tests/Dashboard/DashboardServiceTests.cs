using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Dashboard;
using Relio.Application.Security;
using Relio.Data.Dashboard;
using Relio.Domain;

namespace Relio.Data.Tests.Dashboard;

public sealed class DashboardServiceTests
{
    private const string OwnerA = "dashboard-owner-a";
    private const string OwnerB = "dashboard-owner-b";

    private static readonly DateTimeOffset DefaultNow = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly DefaultToday = new(2026, 10, 7);

    [Fact]
    public async Task GetAsync_requires_an_authenticated_user_before_reading_data()
    {
        var clock = new FakeTimeProvider(DefaultNow);
        await using var dbContext = CreateDbContext(clock);
        var service = CreateService(dbContext, null, clock);

        var act = () => service.GetAsync();

        await act.Should().ThrowAsync<UnauthenticatedUserException>();
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData("Pacific/Kiritimati", 2026, 10, 6, 23, 30, 2026, 10, 7)]
    [InlineData("Pacific/Pago_Pago", 2026, 10, 7, 10, 30, 2026, 10, 6)]
    [InlineData("America/New_York", 2026, 3, 8, 6, 30, 2026, 3, 8)]
    [InlineData("America/New_York", 2026, 3, 8, 7, 30, 2026, 3, 8)]
    public async Task GetAsync_uses_the_user_calendar_for_the_inclusive_reminder_window(
        string timeZoneId,
        int utcYear,
        int utcMonth,
        int utcDay,
        int utcHour,
        int utcMinute,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        var now = new DateTimeOffset(utcYear, utcMonth, utcDay, utcHour, utcMinute, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var dbContext = CreateDbContext(clock);
        var person = new Person { OwnerId = OwnerA, FirstName = "Ada" };
        dbContext.People.Add(person);
        dbContext.UserProfiles.Add(new UserProfile { OwnerId = OwnerA, TimeZoneId = timeZoneId });

        var today = new DateOnly(expectedYear, expectedMonth, expectedDay);
        var reminders = new[]
        {
            NewReminder(OwnerA, person.Id, "Overdue", today.AddDays(-100)),
            NewReminder(OwnerA, person.Id, "Due on day 30", today.AddDays(30)),
            NewReminder(OwnerA, person.Id, "Snoozed to day 30", today.AddDays(-100), today.AddDays(30)),
            NewReminder(OwnerA, person.Id, "Due on day 31", today.AddDays(31)),
            NewReminder(OwnerA, person.Id, "Snoozed to day 31", today.AddDays(-100), today.AddDays(31)),
            NewReminder(OwnerA, person.Id, "Completed", today, isCompleted: true),
        };
        dbContext.Reminders.AddRange(reminders);
        dbContext.People.Add(new Person { OwnerId = OwnerA, FirstName = "Archived", IsArchived = true });
        await dbContext.SaveChangesAsync();
        var archived = await dbContext.People.SingleAsync(candidate => candidate.FirstName == "Archived");
        dbContext.Reminders.Add(NewReminder(OwnerA, archived.Id, "Archived person", today));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var snapshot = await CreateService(dbContext, OwnerA, clock).GetAsync();

        snapshot.Today.Should().Be(today);
        snapshot.UpcomingReminders.Select(reminder => reminder.Title)
            .Should().BeEquivalentTo("Overdue", "Due on day 30", "Snoozed to day 30");
        snapshot.UpcomingReminders.Single(reminder => reminder.Title == "Snoozed to day 30")
            .EffectiveDueDate.Should().Be(today.AddDays(30));
        snapshot.UpcomingReminders.Should().NotContain(reminder =>
            reminder.Title == "Due on day 31"
            || reminder.Title == "Snoozed to day 31"
            || reminder.Title == "Completed"
            || reminder.Title == "Archived person");
        dbContext.ChangeTracker.Entries().Should().BeEmpty("dashboard reads are untracked");
    }

    [Fact]
    public async Task GetAsync_calculates_leap_day_birthdays_and_respects_per_person_lead_and_disabled_settings()
    {
        var now = new DateTimeOffset(2027, 2, 27, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var dbContext = CreateDbContext(clock);
        dbContext.UserProfiles.Add(new UserProfile
        {
            OwnerId = OwnerA,
            TimeZoneId = "UTC",
            DefaultBirthdayLeadDays = 7,
        });
        var leapDay = new Person
        {
            OwnerId = OwnerA,
            FirstName = "Leap",
            BirthdayYear = 1988,
            BirthdayMonth = 2,
            BirthdayDay = 29,
            BirthdayReminderLeadDays = 0,
        };
        var overriddenLead = new Person
        {
            OwnerId = OwnerA,
            FirstName = "Soon",
            BirthdayMonth = 3,
            BirthdayDay = 5,
            BirthdayReminderLeadDays = 2,
        };
        var disabled = new Person
        {
            OwnerId = OwnerA,
            FirstName = "Disabled",
            BirthdayMonth = 2,
            BirthdayDay = 28,
            BirthdayReminderDisabled = true,
        };
        var archived = new Person
        {
            OwnerId = OwnerA,
            FirstName = "Archived",
            BirthdayMonth = 2,
            BirthdayDay = 28,
            IsArchived = true,
        };
        dbContext.People.AddRange(leapDay, overriddenLead, disabled, archived);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var snapshot = await CreateService(dbContext, OwnerA, clock).GetAsync();

        snapshot.Today.Should().Be(new DateOnly(2027, 2, 27));
        snapshot.UpcomingBirthdays.Should().HaveCount(2);
        var leapDayReminder = snapshot.UpcomingBirthdays.Single(birthday => birthday.PersonId == leapDay.Id);
        leapDayReminder.BirthdayDate.Should().Be(new DateOnly(2027, 2, 28));
        leapDayReminder.TurningAge.Should().Be(39);
        leapDayReminder.DaysUntilBirthday.Should().Be(1);
        leapDayReminder.ReminderDate.Should().Be(new DateOnly(2027, 2, 28), "the person's zero-day override wins over the seven-day default");
        leapDayReminder.IsDue.Should().BeFalse();

        var otherBirthday = snapshot.UpcomingBirthdays.Single(birthday => birthday.PersonId == overriddenLead.Id);
        otherBirthday.ReminderDate.Should().Be(new DateOnly(2027, 3, 3));
        otherBirthday.LeadDays.Should().Be(2);
        snapshot.UpcomingBirthdays.Select(birthday => birthday.PersonId)
            .Should().NotContain(disabled.Id, "a person can turn off their own birthday reminder");
        snapshot.UpcomingBirthdays.Select(birthday => birthday.PersonId)
            .Should().NotContain(archived.Id, "archived people do not appear in the dashboard");
    }

    [Fact]
    public async Task GetAsync_omits_birthdays_when_the_current_users_setting_is_disabled()
    {
        var clock = new FakeTimeProvider(DefaultNow);
        await using var dbContext = CreateDbContext(clock);
        dbContext.UserProfiles.Add(new UserProfile
        {
            OwnerId = OwnerA,
            BirthdayRemindersEnabled = false,
        });
        dbContext.People.Add(new Person
        {
            OwnerId = OwnerA,
            FirstName = "Ada",
            BirthdayMonth = 10,
            BirthdayDay = 8,
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var snapshot = await CreateService(dbContext, OwnerA, clock).GetAsync();

        snapshot.UpcomingBirthdays.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_uses_the_persons_user_calendar_creation_date_when_last_contact_is_missing()
    {
        var now = new DateTimeOffset(2026, 10, 7, 0, 30, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var dbContext = CreateDbContext(clock);
        dbContext.UserProfiles.Add(new UserProfile { OwnerId = OwnerA, TimeZoneId = "Pacific/Kiritimati" });
        var exactlyOnCadence = new Person
        {
            OwnerId = OwnerA,
            FirstName = "On cadence",
            StayInTouchCadenceDays = 30,
        };
        var overdue = new Person
        {
            OwnerId = OwnerA,
            FirstName = "Overdue",
            StayInTouchCadenceDays = 30,
        };
        var archived = new Person
        {
            OwnerId = OwnerA,
            FirstName = "Archived",
            StayInTouchCadenceDays = 1,
            IsArchived = true,
        };
        dbContext.People.AddRange(exactlyOnCadence, overdue, archived);
        await dbContext.SaveChangesAsync();

        exactlyOnCadence.CreatedAtUtc = Utc(2026, 9, 6, 23, 30);
        overdue.CreatedAtUtc = Utc(2026, 9, 5, 23, 30);
        archived.CreatedAtUtc = Utc(2026, 9, 1, 12, 0);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var snapshot = await CreateService(dbContext, OwnerA, clock).GetAsync();

        snapshot.Today.Should().Be(new DateOnly(2026, 10, 7));
        snapshot.ReachOuts.Should().ContainSingle();
        snapshot.ReachOuts[0].PersonId.Should().Be(overdue.Id);
        snapshot.ReachOuts[0].ReferenceDate.Should().Be(new DateOnly(2026, 9, 6));
        snapshot.ReachOuts[0].DaysSinceContact.Should().Be(31);
        snapshot.ReachOuts[0].DaysOverdue.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_returns_each_interaction_once_with_only_active_owned_participants()
    {
        var clock = new FakeTimeProvider(DefaultNow);
        await using var dbContext = CreateDbContext(clock);
        var active = new Person { OwnerId = OwnerA, FirstName = "Ada" };
        var archived = new Person { OwnerId = OwnerA, FirstName = "Archived", IsArchived = true };
        var foreign = new Person { OwnerId = OwnerB, FirstName = "Foreign" };
        var mismatchedParticipant = new Person { OwnerId = OwnerA, FirstName = "Wrong owner link" };
        dbContext.People.AddRange(active, archived, foreign, mismatchedParticipant);

        var sharedInteraction = NewInteraction(OwnerA, "Shared description");
        var archivedOnlyInteraction = NewInteraction(OwnerA, "Archived-only description");
        var foreignInteraction = NewInteraction(OwnerB, "Foreign description");
        dbContext.Interactions.AddRange(sharedInteraction, archivedOnlyInteraction, foreignInteraction);
        dbContext.InteractionParticipants.AddRange(
            NewParticipant(OwnerA, sharedInteraction.Id, active.Id),
            NewParticipant(OwnerA, sharedInteraction.Id, archived.Id),
            NewParticipant(OwnerA, sharedInteraction.Id, foreign.Id),
            NewParticipant(OwnerB, sharedInteraction.Id, mismatchedParticipant.Id),
            NewParticipant(OwnerA, archivedOnlyInteraction.Id, archived.Id),
            NewParticipant(OwnerB, foreignInteraction.Id, foreign.Id));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var snapshot = await CreateService(dbContext, OwnerA, clock).GetAsync();

        snapshot.RecentInteractions.Should().ContainSingle();
        snapshot.RecentInteractions[0].Id.Should().Be(sharedInteraction.Id);
        snapshot.RecentInteractions[0].Description.Should().Be("Shared description");
        snapshot.RecentInteractions[0].Participants.Should().ContainSingle();
        snapshot.RecentInteractions[0].Participants[0].PersonId.Should().Be(active.Id);
    }

    [Fact]
    public async Task GetAsync_isolates_every_section_by_owner_and_checks_the_owner_of_related_people()
    {
        var clock = new FakeTimeProvider(DefaultNow);
        await using var dbContext = CreateDbContext(clock);
        dbContext.UserProfiles.AddRange(
            new UserProfile { OwnerId = OwnerA, TimeZoneId = "UTC" },
            new UserProfile { OwnerId = OwnerB, TimeZoneId = "UTC" });

        var personA = new Person
        {
            OwnerId = OwnerA,
            FirstName = "Ada",
            BirthdayMonth = 10,
            BirthdayDay = 15,
            StayInTouchCadenceDays = 30,
            LastContactedOn = DefaultToday.AddDays(-40),
        };
        var archivedA = new Person
        {
            OwnerId = OwnerA,
            FirstName = "Archived",
            IsArchived = true,
            BirthdayMonth = 10,
            BirthdayDay = 15,
            StayInTouchCadenceDays = 1,
            LastContactedOn = DefaultToday.AddDays(-40),
        };
        var personB = new Person
        {
            OwnerId = OwnerB,
            FirstName = "Bea",
            BirthdayMonth = 10,
            BirthdayDay = 16,
            StayInTouchCadenceDays = 30,
            LastContactedOn = DefaultToday.AddDays(-40),
        };
        dbContext.People.AddRange(personA, archivedA, personB);

        var reminderA = NewReminder(OwnerA, personA.Id, "A reminder", DefaultToday.AddDays(2));
        var reminderB = NewReminder(OwnerB, personB.Id, "B reminder", DefaultToday.AddDays(3));
        var reminderForForeignPerson = NewReminder(OwnerA, personB.Id, "Wrong related owner", DefaultToday.AddDays(1));
        var archivedReminder = NewReminder(OwnerA, archivedA.Id, "Archived reminder", DefaultToday);
        dbContext.Reminders.AddRange(reminderA, reminderB, reminderForForeignPerson, archivedReminder);

        var interactionA = NewInteraction(OwnerA, "A interaction");
        var interactionB = NewInteraction(OwnerB, "B interaction");
        dbContext.Interactions.AddRange(interactionA, interactionB);
        dbContext.InteractionParticipants.AddRange(
            NewParticipant(OwnerA, interactionA.Id, personA.Id),
            NewParticipant(OwnerA, interactionA.Id, archivedA.Id),
            NewParticipant(OwnerA, interactionA.Id, personB.Id),
            NewParticipant(OwnerB, interactionA.Id, personA.Id),
            NewParticipant(OwnerB, interactionB.Id, personB.Id));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var snapshotA = await CreateService(dbContext, OwnerA, clock).GetAsync();
        var snapshotB = await CreateService(dbContext, OwnerB, clock).GetAsync();

        snapshotA.ActivePeopleCount.Should().Be(1);
        snapshotA.ArchivedPeopleCount.Should().Be(1);
        snapshotA.UpcomingReminders.Select(reminder => reminder.Id).Should().Equal(reminderA.Id);
        snapshotA.UpcomingBirthdays.Select(birthday => birthday.PersonId).Should().Equal(personA.Id);
        snapshotA.ReachOuts.Select(reachOut => reachOut.PersonId).Should().Equal(personA.Id);
        snapshotA.RecentInteractions.Select(interaction => interaction.Id).Should().Equal(interactionA.Id);
        snapshotA.RecentInteractions[0].Participants.Select(participant => participant.PersonId)
            .Should().Equal(personA.Id);
        snapshotA.RecentlyAddedPeople.Select(person => person.PersonId).Should().Equal(personA.Id);

        snapshotB.ActivePeopleCount.Should().Be(1);
        snapshotB.ArchivedPeopleCount.Should().Be(0);
        snapshotB.UpcomingReminders.Select(reminder => reminder.Id).Should().Equal(reminderB.Id);
        snapshotB.UpcomingBirthdays.Select(birthday => birthday.PersonId).Should().Equal(personB.Id);
        snapshotB.ReachOuts.Select(reachOut => reachOut.PersonId).Should().Equal(personB.Id);
        snapshotB.RecentInteractions.Select(interaction => interaction.Id).Should().Equal(interactionB.Id);
        snapshotB.RecentInteractions[0].Participants.Select(participant => participant.PersonId)
            .Should().Equal(personB.Id);
        snapshotB.RecentlyAddedPeople.Select(person => person.PersonId).Should().Equal(personB.Id);
        dbContext.ChangeTracker.Entries().Should().BeEmpty("both snapshots are read-only");
    }

    [Fact]
    public async Task GetAsync_limits_each_section_to_five_and_orders_ties_stably()
    {
        var clock = new FakeTimeProvider(DefaultNow);
        await using var dbContext = CreateDbContext(clock);
        dbContext.UserProfiles.Add(new UserProfile { OwnerId = OwnerA, TimeZoneId = "UTC" });
        var people = Enumerable.Range(0, 6)
            .Select(index => new Person
            {
                OwnerId = OwnerA,
                FirstName = $"Person {index}",
                BirthdayMonth = 10,
                BirthdayDay = 10,
                StayInTouchCadenceDays = 1,
                LastContactedOn = DefaultToday.AddDays(-5),
            })
            .ToArray();
        dbContext.People.AddRange(people);

        foreach (var person in people)
        {
            dbContext.Reminders.Add(NewReminder(OwnerA, person.Id, "Same due date", DefaultToday.AddDays(2)));
            var interaction = NewInteraction(OwnerA, "Same interaction date", DefaultToday.AddDays(-1));
            dbContext.Interactions.Add(interaction);
            dbContext.InteractionParticipants.Add(NewParticipant(OwnerA, interaction.Id, person.Id));
        }

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var service = CreateService(dbContext, OwnerA, clock);

        var first = await service.GetAsync();
        var second = await service.GetAsync();

        first.UpcomingReminders.Should().HaveCount(5);
        first.UpcomingBirthdays.Should().HaveCount(5);
        first.ReachOuts.Should().HaveCount(5);
        first.RecentInteractions.Should().HaveCount(5);
        first.RecentlyAddedPeople.Should().HaveCount(5);
        first.UpcomingReminders.Select(item => item.Id).Should().OnlyHaveUniqueItems();
        first.UpcomingBirthdays.Select(item => item.PersonId).Should().OnlyHaveUniqueItems();
        first.ReachOuts.Select(item => item.PersonId).Should().OnlyHaveUniqueItems();
        first.RecentInteractions.Select(item => item.Id).Should().OnlyHaveUniqueItems();
        first.RecentlyAddedPeople.Select(item => item.PersonId).Should().OnlyHaveUniqueItems();

        first.UpcomingReminders.Select(item => item.Id).Should().Equal(second.UpcomingReminders.Select(item => item.Id));
        first.UpcomingBirthdays.Select(item => item.PersonId).Should().Equal(second.UpcomingBirthdays.Select(item => item.PersonId));
        first.ReachOuts.Select(item => item.PersonId).Should().Equal(second.ReachOuts.Select(item => item.PersonId));
        first.RecentInteractions.Select(item => item.Id).Should().Equal(second.RecentInteractions.Select(item => item.Id));
        first.RecentlyAddedPeople.Select(item => item.PersonId).Should().Equal(second.RecentlyAddedPeople.Select(item => item.PersonId));
        dbContext.ChangeTracker.Entries().Should().BeEmpty("dashboard reads do not track entities");
    }

    private static RelioDbContext CreateDbContext(TimeProvider timeProvider)
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RelioDbContext(options, timeProvider, FieldProtector);
    }

    private static DashboardService CreateService(
        RelioDbContext dbContext,
        string? ownerId,
        TimeProvider timeProvider) =>
        new(dbContext, new FakeCurrentUser(ownerId), timeProvider);

    private static Reminder NewReminder(
        string ownerId,
        Guid personId,
        string title,
        DateOnly dueDate,
        DateOnly? snoozedUntil = null,
        bool isCompleted = false) =>
        new()
        {
            OwnerId = ownerId,
            PersonId = personId,
            Title = title,
            DueDate = dueDate,
            SnoozedUntilDate = snoozedUntil,
            IsCompleted = isCompleted,
        };

    private static Interaction NewInteraction(
        string ownerId,
        string description,
        DateOnly? occurredOn = null) =>
        new()
        {
            OwnerId = ownerId,
            OccurredOn = occurredOn ?? DefaultToday,
            Kind = InteractionKind.Call,
            Description = description,
        };

    private static InteractionParticipant NewParticipant(string ownerId, Guid interactionId, Guid personId) =>
        new()
        {
            OwnerId = ownerId,
            InteractionId = interactionId,
            PersonId = personId,
        };

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        DateTime.SpecifyKind(new DateTime(year, month, day, hour, minute, 0), DateTimeKind.Utc);

    private sealed class FakeCurrentUser(string? userId) : ICurrentUser
    {
        public bool IsAuthenticated => userId is not null;

        public string? UserId => userId;
    }
}
