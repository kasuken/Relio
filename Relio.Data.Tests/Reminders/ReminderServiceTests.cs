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

    private static RelioDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RelioDbContext(options, TimeProvider.System);
    }

    private static ReminderService CreateService(RelioDbContext dbContext, string userId)
    {
        var fakeCurrentUser = new FakeCurrentUser(userId);
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));
        var timeZoneService = new UserTimeZoneService(dbContext, fakeCurrentUser, timeProvider);
        return new ReminderService(dbContext, fakeCurrentUser, timeZoneService, timeProvider);
    }

    private static async Task<Guid> CreatePersonAsync(
        RelioDbContext dbContext, string ownerId, string firstName, bool isArchived = false)
    {
        var person = new Person
        {
            OwnerId = ownerId,
            FirstName = firstName,
            IsArchived = isArchived,
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
