using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Ownership;
using Relio.Application.Reminders;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Data.Reminders;
using Relio.Data.Time;
using Relio.Domain;

namespace Relio.Data.Tests.Reminders;

public class ReminderServiceTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    [Fact]
    public async Task CreateAsync_and_GetAsync_round_trips_reminder()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Grace");
        var service = CreateService(dbContext, UserA);

        var created = await service.CreateAsync(new CreateReminderRequest
        {
            PersonId = personId,
            Title = "Call Grace",
            DueDate = new DateOnly(2026, 10, 15),
            Frequency = ReminderFrequency.Monthly,
        });

        created.Id.Should().NotBeEmpty();
        created.Title.Should().Be("Call Grace");
        created.PersonDisplayName.Should().Be("Grace");
        created.DueDate.Should().Be(new DateOnly(2026, 10, 15));
        created.Frequency.Should().Be(ReminderFrequency.Monthly);
        created.IsCompleted.Should().BeFalse();

        var fetched = await service.GetAsync(created.Id);
        fetched.Should().NotBeNull();
        fetched!.Title.Should().Be("Call Grace");
    }

    [Fact]
    public async Task User_B_cannot_read_User_A_reminder()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice");
        var serviceA = CreateService(dbContext, UserA);
        var created = await serviceA.CreateAsync(new CreateReminderRequest
        {
            PersonId = personId,
            Title = "Catch up",
            DueDate = new DateOnly(2026, 10, 12),
        });

        var serviceB = CreateService(dbContext, UserB);
        var fetched = await serviceB.GetAsync(created.Id);

        fetched.Should().BeNull();
    }

    [Fact]
    public async Task Creating_reminder_for_another_users_person_throws_ForeignEntityNotOwned()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice");
        var serviceB = CreateService(dbContext, UserB);

        var act = () => serviceB.CreateAsync(new CreateReminderRequest
        {
            PersonId = personId,
            Title = "Catch up",
            DueDate = new DateOnly(2026, 10, 12),
        });

        await act.Should().ThrowAsync<ForeignEntityNotOwnedException>();
    }

    [Fact]
    public async Task Creating_reminder_for_archived_person_throws_ForeignEntityNotOwned()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Alice", isArchived: true);
        var serviceA = CreateService(dbContext, UserA);

        var act = () => serviceA.CreateAsync(new CreateReminderRequest
        {
            PersonId = personId,
            Title = "Catch up",
            DueDate = new DateOnly(2026, 10, 12),
        });

        await act.Should().ThrowAsync<ForeignEntityNotOwnedException>();
    }

    [Fact]
    public async Task CompleteAsync_for_recurring_reminder_advances_due_date()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Bob");
        var service = CreateService(dbContext, UserA);

        var created = await service.CreateAsync(new CreateReminderRequest
        {
            PersonId = personId,
            Title = "Check in",
            DueDate = new DateOnly(2026, 10, 1),
            Frequency = ReminderFrequency.Weekly,
        });

        await service.SnoozeAsync(created.Id, new DateOnly(2026, 10, 5));

        var completed = await service.CompleteAsync(created.Id);
        completed.Should().BeTrue();

        var updated = await service.GetAsync(created.Id);
        updated!.DueDate.Should().Be(new DateOnly(2026, 10, 8));
        updated.SnoozedUntilDate.Should().BeNull();
        updated.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task CompleteAsync_for_one_off_reminder_marks_completed()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Bob");
        var service = CreateService(dbContext, UserA);

        var created = await service.CreateAsync(new CreateReminderRequest
        {
            PersonId = personId,
            Title = "One off task",
            DueDate = new DateOnly(2026, 10, 1),
            Frequency = ReminderFrequency.Once,
        });

        var completed = await service.CompleteAsync(created.Id);
        completed.Should().BeTrue();

        var updated = await service.GetAsync(created.Id);
        updated!.IsCompleted.Should().BeTrue();
        updated.CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task SnoozeAsync_sets_snoozed_date()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Bob");
        var service = CreateService(dbContext, UserA);

        var created = await service.CreateAsync(new CreateReminderRequest
        {
            PersonId = personId,
            Title = "One off task",
            DueDate = new DateOnly(2026, 10, 1),
        });

        var snoozed = await service.SnoozeAsync(created.Id, new DateOnly(2026, 10, 10));
        snoozed.Should().BeTrue();

        var updated = await service.GetAsync(created.Id);
        updated!.SnoozedUntilDate.Should().Be(new DateOnly(2026, 10, 10));
        updated.EffectiveDueDate.Should().Be(new DateOnly(2026, 10, 10));
    }

    [Fact]
    public async Task DeleteAsync_removes_reminder()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Bob");
        var service = CreateService(dbContext, UserA);

        var created = await service.CreateAsync(new CreateReminderRequest
        {
            PersonId = personId,
            Title = "To delete",
            DueDate = new DateOnly(2026, 10, 1),
        });

        var deleted = await service.DeleteAsync(created.Id);
        deleted.Should().BeTrue();

        (await service.GetAsync(created.Id)).Should().BeNull();
    }

    [Fact]
    public async Task ListDueAsync_returns_due_reminders_and_excludes_archived_people()
    {
        await using var dbContext = CreateDbContext();
        var activePerson = await CreatePersonAsync(dbContext, UserA, "Active Alice");
        var archivedPerson = await CreatePersonAsync(dbContext, UserA, "Archived Bob", isArchived: true);
        var service = CreateService(dbContext, UserA);

        var reminder1 = await service.CreateAsync(new CreateReminderRequest
        {
            PersonId = activePerson,
            Title = "Due today",
            DueDate = new DateOnly(2026, 10, 7),
        });

        // Directly attach reminder to archived person in db context for test
        var archivedReminder = new Reminder
        {
            OwnerId = UserA,
            PersonId = archivedPerson,
            Title = "Archived due",
            DueDate = new DateOnly(2026, 10, 7),
        };
        dbContext.Reminders.Add(archivedReminder);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var due = await service.ListDueAsync(new DateOnly(2026, 10, 7));

        due.Select(r => r.Id).Should().Contain(reminder1.Id);
        due.Select(r => r.Id).Should().NotContain(archivedReminder.Id);
    }

    [Fact]
    public async Task ListDueBirthdaysAsync_returns_due_birthdays_and_respects_lead_days()
    {
        await using var dbContext = CreateDbContext();
        // Today is 2026-10-07
        var dueTodayId = await CreatePersonAsync(dbContext, UserA, "Grace", birthdayDay: 7, birthdayMonth: 10, birthdayYear: 1990);
        var dueWithLeadDaysId = await CreatePersonAsync(dbContext, UserA, "Alan", birthdayDay: 14, birthdayMonth: 10, birthdayReminderLeadDays: 7);
        var notDueId = await CreatePersonAsync(dbContext, UserA, "Bob", birthdayDay: 20, birthdayMonth: 10, birthdayReminderLeadDays: 0);

        var service = CreateService(dbContext, UserA);
        var due = await service.ListDueBirthdaysAsync();

        due.Should().HaveCount(2);
        due[0].PersonId.Should().Be(dueTodayId);
        due[0].PersonDisplayName.Should().Be("Grace");
        due[0].DaysUntilBirthday.Should().Be(0);
        due[0].TurningAge.Should().Be(36);
        due[0].IsDue.Should().BeTrue();

        due[1].PersonId.Should().Be(dueWithLeadDaysId);
        due[1].PersonDisplayName.Should().Be("Alan");
        due[1].DaysUntilBirthday.Should().Be(7);
        due[1].TurningAge.Should().BeNull();
        due[1].IsDue.Should().BeTrue();

        due.Select(d => d.PersonId).Should().NotContain(notDueId);
    }

    [Fact]
    public async Task ListDueBirthdaysAsync_returns_empty_when_globally_disabled()
    {
        await using var dbContext = CreateDbContext();
        await CreatePersonAsync(dbContext, UserA, "Grace", birthdayDay: 7, birthdayMonth: 10);
        dbContext.UserProfiles.Add(new UserProfile { OwnerId = UserA, BirthdayRemindersEnabled = false });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = CreateService(dbContext, UserA);
        var due = await service.ListDueBirthdaysAsync();

        due.Should().BeEmpty();
    }

    [Fact]
    public async Task ListDueBirthdaysAsync_respects_per_person_disabled_and_archived()
    {
        await using var dbContext = CreateDbContext();
        await CreatePersonAsync(dbContext, UserA, "Grace", birthdayDay: 7, birthdayMonth: 10, birthdayReminderDisabled: true);
        await CreatePersonAsync(dbContext, UserA, "Archived", birthdayDay: 7, birthdayMonth: 10, isArchived: true);

        var service = CreateService(dbContext, UserA);
        var due = await service.ListDueBirthdaysAsync();

        due.Should().BeEmpty();
    }

    [Fact]
    public async Task ListUpcomingBirthdaysAsync_filters_by_daysAhead_and_orders_by_date_and_name()
    {
        await using var dbContext = CreateDbContext();
        // Today is 2026-10-07
        var person1 = await CreatePersonAsync(dbContext, UserA, "Zara", birthdayDay: 10, birthdayMonth: 10);
        var person2 = await CreatePersonAsync(dbContext, UserA, "Adam", birthdayDay: 10, birthdayMonth: 10);
        var person3 = await CreatePersonAsync(dbContext, UserA, "Far", birthdayDay: 1, birthdayMonth: 12); // > 30 days ahead

        var service = CreateService(dbContext, UserA);
        var upcoming30 = await service.ListUpcomingBirthdaysAsync(daysAhead: 30);

        upcoming30.Should().HaveCount(2);
        // Same date: ordered by name (Adam, then Zara)
        upcoming30[0].PersonId.Should().Be(person2);
        upcoming30[1].PersonId.Should().Be(person1);

        var upcoming2 = await service.ListUpcomingBirthdaysAsync(daysAhead: 2);
        upcoming2.Should().BeEmpty();
    }

    [Fact]
    public async Task User_isolation_User_B_cannot_read_User_A_birthdays()
    {
        await using var dbContext = CreateDbContext();
        await CreatePersonAsync(dbContext, UserA, "Grace", birthdayDay: 7, birthdayMonth: 10);

        var serviceB = CreateService(dbContext, UserB);
        var due = await serviceB.ListDueBirthdaysAsync();
        var upcoming = await serviceB.ListUpcomingBirthdaysAsync();

        due.Should().BeEmpty();
        upcoming.Should().BeEmpty();
    }

    [Fact]
    public async Task ListOverdueReachOutsAsync_returns_people_overdue_for_contact()
    {
        await using var dbContext = CreateDbContext();
        var today = new DateOnly(2026, 10, 7);

        // Grace: 30-day cadence, contacted 31 days ago -> overdue
        var graceId = await CreatePersonAsync(dbContext, UserA, "Grace",
            stayInTouchCadenceDays: 30, lastContactedOn: today.AddDays(-31));

        // Ada: 30-day cadence, contacted 30 days ago -> not overdue
        await CreatePersonAsync(dbContext, UserA, "Ada",
            stayInTouchCadenceDays: 30, lastContactedOn: today.AddDays(-30));

        // Alan: 30-day cadence, contacted 40 days ago, but archived -> ignored
        await CreatePersonAsync(dbContext, UserA, "Alan", isArchived: true,
            stayInTouchCadenceDays: 30, lastContactedOn: today.AddDays(-40));

        // Margaret: no cadence -> ignored
        await CreatePersonAsync(dbContext, UserA, "Margaret",
            stayInTouchCadenceDays: null, lastContactedOn: today.AddDays(-100));

        var service = CreateService(dbContext, UserA);
        var reachOuts = await service.ListOverdueReachOutsAsync();

        reachOuts.Should().ContainSingle();
        var reachOut = reachOuts.Single();
        reachOut.PersonId.Should().Be(graceId);
        reachOut.PersonDisplayName.Should().Be("Grace");
        reachOut.CadenceDays.Should().Be(30);
        reachOut.DaysSinceContact.Should().Be(31);
        reachOut.DaysOverdue.Should().Be(1);
    }

    [Fact]
    public async Task MarkContactedAsync_updates_last_contacted_and_removes_from_reach_out()
    {
        await using var dbContext = CreateDbContext();
        var today = new DateOnly(2026, 10, 7);
        var personId = await CreatePersonAsync(dbContext, UserA, "Grace",
            stayInTouchCadenceDays: 30, lastContactedOn: today.AddDays(-35));

        var service = CreateService(dbContext, UserA);
        var before = await service.ListOverdueReachOutsAsync();
        before.Should().ContainSingle();

        var marked = await service.MarkContactedAsync(personId);
        marked.Should().BeTrue();

        var after = await service.ListOverdueReachOutsAsync();
        after.Should().BeEmpty();

        var person = await dbContext.People.AsNoTracking().SingleAsync(p => p.Id == personId);
        person.LastContactedOn.Should().Be(today);
    }

    [Fact]
    public async Task MarkContactedAsync_with_explicit_date_sets_provided_date()
    {
        await using var dbContext = CreateDbContext();
        var today = new DateOnly(2026, 10, 7);
        var personId = await CreatePersonAsync(dbContext, UserA, "Grace",
            stayInTouchCadenceDays: 30, lastContactedOn: today.AddDays(-35));

        var explicitDate = new DateOnly(2026, 10, 5);
        var service = CreateService(dbContext, UserA);
        var marked = await service.MarkContactedAsync(personId, explicitDate);
        marked.Should().BeTrue();

        var person = await dbContext.People.AsNoTracking().SingleAsync(p => p.Id == personId);
        person.LastContactedOn.Should().Be(explicitDate);
    }

    [Fact]
    public async Task MarkContactedAsync_returns_false_when_person_not_found_or_not_owned()
    {
        await using var dbContext = CreateDbContext();
        var personId = await CreatePersonAsync(dbContext, UserA, "Grace",
            stayInTouchCadenceDays: 30);

        var serviceA = CreateService(dbContext, UserA);
        var nonexistent = await serviceA.MarkContactedAsync(Guid.NewGuid());
        nonexistent.Should().BeFalse();

        var serviceB = CreateService(dbContext, UserB);
        var notOwned = await serviceB.MarkContactedAsync(personId);
        notOwned.Should().BeFalse();
    }

    [Fact]
    public async Task User_isolation_User_B_cannot_read_User_A_reach_outs()
    {
        await using var dbContext = CreateDbContext();
        var today = new DateOnly(2026, 10, 7);
        await CreatePersonAsync(dbContext, UserA, "Grace",
            stayInTouchCadenceDays: 30, lastContactedOn: today.AddDays(-40));

        var serviceB = CreateService(dbContext, UserB);
        var reachOuts = await serviceB.ListOverdueReachOutsAsync();

        reachOuts.Should().BeEmpty();
    }

    private static RelioDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RelioDbContext(options, TimeProvider.System, FieldProtector);
    }

    private static ReminderService CreateService(RelioDbContext dbContext, string userId)
    {
        var fakeCurrentUser = new FakeCurrentUser(userId);
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));
        var timeZoneService = new UserTimeZoneService(dbContext, fakeCurrentUser, timeProvider);
        return new ReminderService(dbContext, fakeCurrentUser, timeZoneService, timeProvider);
    }

    private static async Task<Guid> CreatePersonAsync(
        RelioDbContext dbContext,
        string ownerId,
        string firstName,
        bool isArchived = false,
        int? birthdayDay = null,
        int? birthdayMonth = null,
        int? birthdayYear = null,
        bool birthdayReminderDisabled = false,
        int? birthdayReminderLeadDays = null,
        int? stayInTouchCadenceDays = null,
        DateOnly? lastContactedOn = null)
    {
        var person = new Person
        {
            OwnerId = ownerId,
            FirstName = firstName,
            IsArchived = isArchived,
            BirthdayDay = birthdayDay,
            BirthdayMonth = birthdayMonth,
            BirthdayYear = birthdayYear,
            BirthdayReminderDisabled = birthdayReminderDisabled,
            BirthdayReminderLeadDays = birthdayReminderLeadDays,
            StayInTouchCadenceDays = stayInTouchCadenceDays,
            LastContactedOn = lastContactedOn,
        };
        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return person.Id;
    }

    private sealed class FakeCurrentUser(string? userId) : ICurrentUser
    {
        public string? UserId => userId;
        public bool IsAuthenticated => userId is not null;
    }
}
