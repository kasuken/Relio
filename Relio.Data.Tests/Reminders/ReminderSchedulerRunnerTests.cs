using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Reminders;
using Relio.Application.Time;
using Relio.Data.Identity;
using Relio.Data.Reminders;
using Relio.Domain;

namespace Relio.Data.Tests.Reminders;

public class ReminderSchedulerRunnerTests
{
    [Fact]
    public async Task Exact_once_delivery_for_immediate_mode()
    {
        var dbName = Guid.NewGuid().ToString();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));
        var emailSender = new FakeReminderEmailSender();

        await using (var dbContext = CreateDbContext(dbName, timeProvider))
        {
            var user = CreateUser("user-1", "user1@example.com", emailConfirmed: true);
            var profile = new UserProfile
            {
                OwnerId = user.Id,
                TimeZoneId = "UTC",
                ReminderEmailDelivery = ReminderEmailDelivery.Immediate,
                UnsubscribeToken = "tok_user1",
            };
            var person = new Person
            {
                OwnerId = user.Id,
                FirstName = "Grace",
                LastName = "Hopper",
            };
            var reminder1 = new Reminder
            {
                OwnerId = user.Id,
                Person = person,
                Title = "Call Grace",
                DueDate = new DateOnly(2026, 10, 7),
            };
            var reminder2 = new Reminder
            {
                OwnerId = user.Id,
                Person = person,
                Title = "Send flowers",
                DueDate = new DateOnly(2026, 10, 6),
            };

            dbContext.Users.Add(user);
            dbContext.UserProfiles.Add(profile);
            dbContext.People.Add(person);
            dbContext.Reminders.AddRange(reminder1, reminder2);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext(dbName, timeProvider))
        {
            var runner = new ReminderSchedulerRunner(
                dbContext,
                emailSender,
                timeProvider,
                NullLogger<ReminderSchedulerRunner>.Instance);

            var deliveredCount = await runner.RunDueRemindersJobAsync();
            deliveredCount.Should().Be(2);

            emailSender.ImmediateEmails.Should().HaveCount(2);
            emailSender.ImmediateEmails.Should().Contain(e =>
                e.ToEmail == "user1@example.com"
                && e.PersonName == "Grace Hopper"
                && e.ReminderTitle == "Call Grace"
                && e.DueDate == new DateOnly(2026, 10, 7)
                && e.UnsubscribeToken == "tok_user1");
            emailSender.ImmediateEmails.Should().Contain(e =>
                e.ToEmail == "user1@example.com"
                && e.PersonName == "Grace Hopper"
                && e.ReminderTitle == "Send flowers"
                && e.DueDate == new DateOnly(2026, 10, 6)
                && e.UnsubscribeToken == "tok_user1");

            var reminders = await dbContext.Reminders.ToListAsync();
            reminders.Should().AllSatisfy(r => r.LastDeliveredDate.Should().Be(new DateOnly(2026, 10, 7)));

            // Run again on the same day -> exact-once delivery, 0 emails sent, 0 returned
            var secondRunCount = await runner.RunDueRemindersJobAsync();
            secondRunCount.Should().Be(0);
            emailSender.ImmediateEmails.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task Exact_once_delivery_for_daily_digest_mode()
    {
        var dbName = Guid.NewGuid().ToString();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));
        var emailSender = new FakeReminderEmailSender();

        await using (var dbContext = CreateDbContext(dbName, timeProvider))
        {
            var user = CreateUser("user-2", "digest@example.com", emailConfirmed: true);
            var profile = new UserProfile
            {
                OwnerId = user.Id,
                TimeZoneId = "UTC",
                ReminderEmailDelivery = ReminderEmailDelivery.DailyDigest,
                UnsubscribeToken = "tok_digest",
            };
            var person = new Person
            {
                OwnerId = user.Id,
                FirstName = "Alan",
                LastName = "Turing",
            };
            var reminder1 = new Reminder
            {
                OwnerId = user.Id,
                Person = person,
                Title = "Coffee chat",
                DueDate = new DateOnly(2026, 10, 5),
            };
            var reminder2 = new Reminder
            {
                OwnerId = user.Id,
                Person = person,
                Title = "Check in on thesis",
                DueDate = new DateOnly(2026, 10, 7),
            };

            dbContext.Users.Add(user);
            dbContext.UserProfiles.Add(profile);
            dbContext.People.Add(person);
            dbContext.Reminders.AddRange(reminder1, reminder2);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateDbContext(dbName, timeProvider))
        {
            var runner = new ReminderSchedulerRunner(
                dbContext,
                emailSender,
                timeProvider,
                NullLogger<ReminderSchedulerRunner>.Instance);

            var deliveredCount = await runner.RunDueRemindersJobAsync();
            deliveredCount.Should().Be(2);

            emailSender.DigestEmails.Should().HaveCount(1);
            var digest = emailSender.DigestEmails.Single();
            digest.ToEmail.Should().Be("digest@example.com");
            digest.UnsubscribeToken.Should().Be("tok_digest");
            digest.Items.Should().HaveCount(2);
            digest.Items[0].Title.Should().Be("Coffee chat");
            digest.Items[0].DueDate.Should().Be(new DateOnly(2026, 10, 5));
            digest.Items[0].PersonName.Should().Be("Alan Turing");
            digest.Items[1].Title.Should().Be("Check in on thesis");
            digest.Items[1].DueDate.Should().Be(new DateOnly(2026, 10, 7));
            digest.Items[1].PersonName.Should().Be("Alan Turing");

            var reminders = await dbContext.Reminders.ToListAsync();
            reminders.Should().AllSatisfy(r => r.LastDeliveredDate.Should().Be(new DateOnly(2026, 10, 7)));

            // Running again on the same day produces 0 duplicates
            var secondRun = await runner.RunDueRemindersJobAsync();
            secondRun.Should().Be(0);
            emailSender.DigestEmails.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task Restarting_the_app_does_not_resend_reminders_on_the_same_day()
    {
        var dbName = Guid.NewGuid().ToString();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero));
        var emailSender = new FakeReminderEmailSender();

        // 1. Initial run delivers the reminder
        await using (var dbContext = CreateDbContext(dbName, timeProvider))
        {
            var user = CreateUser("user-restart", "restart@example.com", emailConfirmed: true);
            var profile = new UserProfile
            {
                OwnerId = user.Id,
                TimeZoneId = "UTC",
                ReminderEmailDelivery = ReminderEmailDelivery.Immediate,
            };
            var person = new Person { OwnerId = user.Id, FirstName = "Ada" };
            var reminder = new Reminder
            {
                OwnerId = user.Id,
                Person = person,
                Title = "Discuss note G",
                DueDate = new DateOnly(2026, 10, 7),
            };

            dbContext.Users.Add(user);
            dbContext.UserProfiles.Add(profile);
            dbContext.People.Add(person);
            dbContext.Reminders.Add(reminder);
            await dbContext.SaveChangesAsync();

            var runner1 = new ReminderSchedulerRunner(
                dbContext,
                emailSender,
                timeProvider,
                NullLogger<ReminderSchedulerRunner>.Instance);

            var delivered = await runner1.RunDueRemindersJobAsync();
            delivered.Should().Be(1);
            emailSender.ImmediateEmails.Should().HaveCount(1);
        }

        // 2. Simulate application restart: fresh DbContext instance connected to the same database, fresh runner
        await using (var dbContext = CreateDbContext(dbName, timeProvider))
        {
            var runner2 = new ReminderSchedulerRunner(
                dbContext,
                emailSender,
                timeProvider,
                NullLogger<ReminderSchedulerRunner>.Instance);

            var delivered = await runner2.RunDueRemindersJobAsync();
            delivered.Should().Be(0);
            emailSender.ImmediateEmails.Should().HaveCount(1, "restarting the app must not resend reminders");
        }
    }

    [Fact]
    public async Task Different_user_time_zones_evaluate_due_date_correctly()
    {
        var dbName = Guid.NewGuid().ToString();
        // Base instant: 2026-10-07 10:30:00 UTC
        // Pacific/Kiritimati (UTC+14): local time is 2026-10-08 00:30:00 -> date is 2026-10-08
        // Pacific/Pago_Pago  (UTC-11): local time is 2026-10-06 23:30:00 -> date is 2026-10-06
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 30, 0, TimeSpan.Zero));
        var emailSender = new FakeReminderEmailSender();

        await using (var dbContext = CreateDbContext(dbName, timeProvider))
        {
            var userKiritimati = CreateUser("user-kiri", "kiri@example.com", emailConfirmed: true);
            var userPago = CreateUser("user-pago", "pago@example.com", emailConfirmed: true);

            var profileKiri = new UserProfile
            {
                OwnerId = userKiritimati.Id,
                TimeZoneId = "Pacific/Kiritimati",
                ReminderEmailDelivery = ReminderEmailDelivery.Immediate,
            };
            var profilePago = new UserProfile
            {
                OwnerId = userPago.Id,
                TimeZoneId = "Pacific/Pago_Pago",
                ReminderEmailDelivery = ReminderEmailDelivery.Immediate,
            };

            var personKiri = new Person { OwnerId = userKiritimati.Id, FirstName = "KiriFriend" };
            var personPago = new Person { OwnerId = userPago.Id, FirstName = "PagoFriend" };

            // Reminder due on 2026-10-07:
            // For Kiritimati (today = 2026-10-07), this IS due!
            // For Pago Pago (today = 2026-10-06), this is NOT due yet!
            var reminderKiri = new Reminder
            {
                OwnerId = userKiritimati.Id,
                Person = personKiri,
                Title = "Kiritimati reminder",
                DueDate = new DateOnly(2026, 10, 7),
            };
            var reminderPago = new Reminder
            {
                OwnerId = userPago.Id,
                Person = personPago,
                Title = "Pago reminder",
                DueDate = new DateOnly(2026, 10, 7),
            };

            dbContext.Users.AddRange(userKiritimati, userPago);
            dbContext.UserProfiles.AddRange(profileKiri, profilePago);
            dbContext.People.AddRange(personKiri, personPago);
            dbContext.Reminders.AddRange(reminderKiri, reminderPago);
            await dbContext.SaveChangesAsync();
        }

        // Run 1: at 09:00 UTC
        await using (var dbContext = CreateDbContext(dbName, timeProvider))
        {
            var runner = new ReminderSchedulerRunner(
                dbContext,
                emailSender,
                timeProvider,
                NullLogger<ReminderSchedulerRunner>.Instance);

            var delivered = await runner.RunDueRemindersJobAsync();
            delivered.Should().Be(1);

            emailSender.ImmediateEmails.Should().ContainSingle(e => e.ToEmail == "kiri@example.com");
            emailSender.ImmediateEmails.Should().NotContain(e => e.ToEmail == "pago@example.com");

            var kiriRem = await dbContext.Reminders.SingleAsync(r => r.Title == "Kiritimati reminder");
            kiriRem.LastDeliveredDate.Should().Be(new DateOnly(2026, 10, 8));

            var pagoRem = await dbContext.Reminders.SingleAsync(r => r.Title == "Pago reminder");
            pagoRem.LastDeliveredDate.Should().BeNull();
        }

        // Advance time: 2 hours later (12:30:00 UTC).
        // Pago Pago (-11) is now 2026-10-07 01:30:00 -> date is 2026-10-07!
        // Kiritimati (+14) is now 2026-10-08 02:30:00 -> date is still 2026-10-08!
        timeProvider.Advance(TimeSpan.FromHours(2));

        // Run 2: now Pago Pago enters 2026-10-07 and its reminder is due!
        await using (var dbContext = CreateDbContext(dbName, timeProvider))
        {
            var runner = new ReminderSchedulerRunner(
                dbContext,
                emailSender,
                timeProvider,
                NullLogger<ReminderSchedulerRunner>.Instance);

            var delivered = await runner.RunDueRemindersJobAsync();
            delivered.Should().Be(1);

            emailSender.ImmediateEmails.Should().Contain(e => e.ToEmail == "pago@example.com");
            // Kiritimati was already delivered on 2026-10-07, so total immediate emails is now 2 (1 kiri + 1 pago)
            emailSender.ImmediateEmails.Should().HaveCount(2);

            var pagoRem = await dbContext.Reminders.SingleAsync(r => r.Title == "Pago reminder");
            pagoRem.LastDeliveredDate.Should().Be(new DateOnly(2026, 10, 7));
        }
    }

    [Fact]
    public async Task Skips_unconfirmed_disabled_or_empty_email_users()
    {
        var dbName = Guid.NewGuid().ToString();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));
        var emailSender = new FakeReminderEmailSender();

        await using (var dbContext = CreateDbContext(dbName, timeProvider))
        {
            var unconfirmedUser = CreateUser("user-unconfirmed", "unconfirmed@example.com", emailConfirmed: false);
            var disabledUser = CreateUser("user-disabled", "disabled@example.com", emailConfirmed: true, isDisabled: true);
            var emptyEmailUser = CreateUser("user-empty", "", emailConfirmed: true);

            var profile1 = new UserProfile { OwnerId = unconfirmedUser.Id, ReminderEmailDelivery = ReminderEmailDelivery.Immediate };
            var profile2 = new UserProfile { OwnerId = disabledUser.Id, ReminderEmailDelivery = ReminderEmailDelivery.Immediate };
            var profile3 = new UserProfile { OwnerId = emptyEmailUser.Id, ReminderEmailDelivery = ReminderEmailDelivery.Immediate };

            var person1 = new Person { OwnerId = unconfirmedUser.Id, FirstName = "P1" };
            var person2 = new Person { OwnerId = disabledUser.Id, FirstName = "P2" };
            var person3 = new Person { OwnerId = emptyEmailUser.Id, FirstName = "P3" };

            var rem1 = new Reminder { OwnerId = unconfirmedUser.Id, Person = person1, Title = "R1", DueDate = new DateOnly(2026, 10, 7) };
            var rem2 = new Reminder { OwnerId = disabledUser.Id, Person = person2, Title = "R2", DueDate = new DateOnly(2026, 10, 7) };
            var rem3 = new Reminder { OwnerId = emptyEmailUser.Id, Person = person3, Title = "R3", DueDate = new DateOnly(2026, 10, 7) };

            dbContext.Users.AddRange(unconfirmedUser, disabledUser, emptyEmailUser);
            dbContext.UserProfiles.AddRange(profile1, profile2, profile3);
            dbContext.People.AddRange(person1, person2, person3);
            dbContext.Reminders.AddRange(rem1, rem2, rem3);
            await dbContext.SaveChangesAsync();

            var runner = new ReminderSchedulerRunner(
                dbContext,
                emailSender,
                timeProvider,
                NullLogger<ReminderSchedulerRunner>.Instance);

            var delivered = await runner.RunDueRemindersJobAsync();
            delivered.Should().Be(0);
            emailSender.ImmediateEmails.Should().BeEmpty();

            var reminders = await dbContext.Reminders.ToListAsync();
            reminders.Should().AllSatisfy(r => r.LastDeliveredDate.Should().BeNull());
        }
    }

    [Fact]
    public async Task Skips_completed_and_future_snoozed_reminders()
    {
        var dbName = Guid.NewGuid().ToString();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));
        var emailSender = new FakeReminderEmailSender();

        await using (var dbContext = CreateDbContext(dbName, timeProvider))
        {
            var user = CreateUser("user-snooze", "snooze@example.com", emailConfirmed: true);
            var profile = new UserProfile { OwnerId = user.Id, ReminderEmailDelivery = ReminderEmailDelivery.Immediate };
            var person = new Person { OwnerId = user.Id, FirstName = "Friend" };

            // Completed reminder:
            var completed = new Reminder
            {
                OwnerId = user.Id,
                Person = person,
                Title = "Completed reminder",
                DueDate = new DateOnly(2026, 10, 7),
                IsCompleted = true,
            };

            // Snoozed to the future:
            var snoozedFuture = new Reminder
            {
                OwnerId = user.Id,
                Person = person,
                Title = "Snoozed to tomorrow",
                DueDate = new DateOnly(2026, 10, 6),
                SnoozedUntilDate = new DateOnly(2026, 10, 8),
            };

            // Snoozed to today (should be delivered!):
            var snoozedToday = new Reminder
            {
                OwnerId = user.Id,
                Person = person,
                Title = "Snoozed to today",
                DueDate = new DateOnly(2026, 10, 1),
                SnoozedUntilDate = new DateOnly(2026, 10, 7),
            };

            dbContext.Users.Add(user);
            dbContext.UserProfiles.Add(profile);
            dbContext.People.Add(person);
            dbContext.Reminders.AddRange(completed, snoozedFuture, snoozedToday);
            await dbContext.SaveChangesAsync();

            var runner = new ReminderSchedulerRunner(
                dbContext,
                emailSender,
                timeProvider,
                NullLogger<ReminderSchedulerRunner>.Instance);

            var delivered = await runner.RunDueRemindersJobAsync();
            delivered.Should().Be(1);

            emailSender.ImmediateEmails.Should().ContainSingle(e =>
                e.ReminderTitle == "Snoozed to today"
                && e.DueDate == new DateOnly(2026, 10, 7));

            var snoozedTodayRem = await dbContext.Reminders.SingleAsync(r => r.Title == "Snoozed to today");
            snoozedTodayRem.LastDeliveredDate.Should().Be(new DateOnly(2026, 10, 7));

            var snoozedFutureRem = await dbContext.Reminders.SingleAsync(r => r.Title == "Snoozed to tomorrow");
            snoozedFutureRem.LastDeliveredDate.Should().BeNull();
        }
    }

    [Fact]
    public async Task Skips_reminders_for_archived_person()
    {
        var dbName = Guid.NewGuid().ToString();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));
        var emailSender = new FakeReminderEmailSender();

        await using (var dbContext = CreateDbContext(dbName, timeProvider))
        {
            var user = CreateUser("user-archived", "archived@example.com", emailConfirmed: true);
            var profile = new UserProfile { OwnerId = user.Id, ReminderEmailDelivery = ReminderEmailDelivery.Immediate };
            var person = new Person { OwnerId = user.Id, FirstName = "ArchivedPerson", IsArchived = true };

            var reminder = new Reminder
            {
                OwnerId = user.Id,
                Person = person,
                Title = "Don't remind me of archived friend",
                DueDate = new DateOnly(2026, 10, 7),
            };

            dbContext.Users.Add(user);
            dbContext.UserProfiles.Add(profile);
            dbContext.People.Add(person);
            dbContext.Reminders.Add(reminder);
            await dbContext.SaveChangesAsync();

            var runner = new ReminderSchedulerRunner(
                dbContext,
                emailSender,
                timeProvider,
                NullLogger<ReminderSchedulerRunner>.Instance);

            var delivered = await runner.RunDueRemindersJobAsync();
            delivered.Should().Be(0);
            emailSender.ImmediateEmails.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Skips_users_with_delivery_None()
    {
        var dbName = Guid.NewGuid().ToString();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));
        var emailSender = new FakeReminderEmailSender();

        await using (var dbContext = CreateDbContext(dbName, timeProvider))
        {
            var user = CreateUser("user-none", "none@example.com", emailConfirmed: true);
            var profile = new UserProfile { OwnerId = user.Id, ReminderEmailDelivery = ReminderEmailDelivery.None };
            var person = new Person { OwnerId = user.Id, FirstName = "Friend" };

            var reminder = new Reminder
            {
                OwnerId = user.Id,
                Person = person,
                Title = "No email delivery desired",
                DueDate = new DateOnly(2026, 10, 7),
            };

            dbContext.Users.Add(user);
            dbContext.UserProfiles.Add(profile);
            dbContext.People.Add(person);
            dbContext.Reminders.Add(reminder);
            await dbContext.SaveChangesAsync();

            var runner = new ReminderSchedulerRunner(
                dbContext,
                emailSender,
                timeProvider,
                NullLogger<ReminderSchedulerRunner>.Instance);

            var delivered = await runner.RunDueRemindersJobAsync();
            delivered.Should().Be(0);
            emailSender.ImmediateEmails.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Responds_to_cancellation()
    {
        var dbName = Guid.NewGuid().ToString();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));
        var emailSender = new FakeReminderEmailSender();

        await using var dbContext = CreateDbContext(dbName, timeProvider);
        var runner = new ReminderSchedulerRunner(
            dbContext,
            emailSender,
            timeProvider,
            NullLogger<ReminderSchedulerRunner>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await runner.RunDueRemindersJobAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static RelioDbContext CreateDbContext(string dbName, TimeProvider timeProvider)
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RelioDbContext(options, timeProvider, FieldProtector);
    }

    private static RelioUser CreateUser(string id, string email, bool emailConfirmed = true, bool isDisabled = false) =>
        new()
        {
            Id = id,
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = emailConfirmed,
            IsDisabled = isDisabled,
        };
}
