using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Relio.Application.Reminders;
using Relio.Data.Identity;
using Relio.Data.Reminders;
using Relio.Data.Tests.Infrastructure;
using Relio.Domain;

namespace Relio.Data.Tests.Reminders;

/// <summary>Shared setup for the reminder scheduler test classes.</summary>
internal static class ReminderSchedulerTestData
{
    public static DbContextOptions<RelioDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    public static RelioDbContext CreateDbContext(
        DbContextOptions<RelioDbContext> database,
        TimeProvider clock,
        params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<RelioDbContext>(database);
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new RelioDbContext(builder.Options, clock, DataProtectionTestHarness.FieldProtector);
    }

    public static RelioUser CreateUser(string id, string email, bool emailConfirmed = true, bool isDisabled = false) =>
        new()
        {
            Id = id,
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = emailConfirmed,
            IsDisabled = isDisabled,
        };

    public static ReminderSchedulerRunner CreateRunner(
        RelioDbContext dbContext,
        FakeReminderEmailSender sender,
        TimeProvider clock,
        ILogger<ReminderSchedulerRunner>? logger = null) =>
        new(dbContext, sender, clock, logger ?? NullLogger<ReminderSchedulerRunner>.Instance);

    /// <summary>Creates a confirmed user and its profile.</summary>
    public static async Task SeedOwnerAsync(
        DbContextOptions<RelioDbContext> database,
        TimeProvider clock,
        string userId,
        ReminderEmailDelivery delivery,
        string timeZoneId = "UTC",
        bool birthdayRemindersEnabled = true,
        int defaultBirthdayLeadDays = 0)
    {
        await using var dbContext = CreateDbContext(database, clock);
        dbContext.Users.Add(CreateUser(userId, $"{userId}@example.com"));
        dbContext.UserProfiles.Add(new UserProfile
        {
            OwnerId = userId,
            TimeZoneId = timeZoneId,
            ReminderEmailDelivery = delivery,
            BirthdayRemindersEnabled = birthdayRemindersEnabled,
            DefaultBirthdayLeadDays = defaultBirthdayLeadDays,
            UnsubscribeToken = $"tok_{userId}",
        });
        await dbContext.SaveChangesAsync();
    }

    public static string EmailOf(string userId) => $"{userId}@example.com";

    public static async Task<Person> AddPersonAsync(
        DbContextOptions<RelioDbContext> database,
        TimeProvider clock,
        string userId,
        string firstName,
        string? lastName = null,
        Action<Person>? configure = null)
    {
        await using var dbContext = CreateDbContext(database, clock);
        var person = new Person { OwnerId = userId, FirstName = firstName, LastName = lastName };
        configure?.Invoke(person);
        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync();
        return person;
    }

    public static async Task<Reminder> AddReminderAsync(
        DbContextOptions<RelioDbContext> database,
        TimeProvider clock,
        string userId,
        Guid personId,
        string title,
        DateOnly dueDate,
        Action<Reminder>? configure = null)
    {
        await using var dbContext = CreateDbContext(database, clock);
        var reminder = new Reminder { OwnerId = userId, PersonId = personId, Title = title, DueDate = dueDate };
        configure?.Invoke(reminder);
        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync();
        return reminder;
    }

    /// <summary>Applies a change to a stored reminder from a separate context (a user edit).</summary>
    public static async Task UpdateReminderAsync(
        DbContextOptions<RelioDbContext> database,
        TimeProvider clock,
        Guid reminderId,
        Action<Reminder> change)
    {
        await using var dbContext = CreateDbContext(database, clock);
        var reminder = await dbContext.Reminders.SingleAsync(r => r.Id == reminderId);
        change(reminder);
        await dbContext.SaveChangesAsync();
    }

    public static async Task UpdatePersonAsync(
        DbContextOptions<RelioDbContext> database,
        TimeProvider clock,
        Guid personId,
        Action<Person> change)
    {
        await using var dbContext = CreateDbContext(database, clock);
        var person = await dbContext.People.SingleAsync(p => p.Id == personId);
        change(person);
        await dbContext.SaveChangesAsync();
    }

    public static async Task UpdateProfileAsync(
        DbContextOptions<RelioDbContext> database,
        TimeProvider clock,
        string userId,
        Action<UserProfile> change)
    {
        await using var dbContext = CreateDbContext(database, clock);
        var profile = await dbContext.UserProfiles.SingleAsync(p => p.OwnerId == userId);
        change(profile);
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Runs the job once on a brand new context (a fresh process).</summary>
    public static async Task<int> RunOnNewContextAsync(
        DbContextOptions<RelioDbContext> database,
        TimeProvider clock,
        FakeReminderEmailSender sender,
        CancellationToken cancellationToken = default,
        params IInterceptor[] interceptors)
    {
        await using var dbContext = CreateDbContext(database, clock, interceptors);
        return await CreateRunner(dbContext, sender, clock).RunDueRemindersJobAsync(cancellationToken);
    }

    public static async Task<List<ReminderDelivery>> LedgerAsync(
        DbContextOptions<RelioDbContext> database,
        TimeProvider clock)
    {
        await using var dbContext = CreateDbContext(database, clock);
        return await dbContext.ReminderDeliveries.AsNoTracking().ToListAsync();
    }

    public static async Task<Reminder> GetReminderAsync(
        DbContextOptions<RelioDbContext> database,
        TimeProvider clock,
        Guid reminderId)
    {
        await using var dbContext = CreateDbContext(database, clock);
        return await dbContext.Reminders.AsNoTracking().SingleAsync(r => r.Id == reminderId);
    }
}
