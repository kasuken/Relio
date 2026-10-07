using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relio.Application.Reminders;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.Reminders;
using Relio.Data.Time;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Isolation;

[Collection(SqlServerCollection.Name)]
public sealed class ReminderCapabilitySqlIsolationTests(SqlServerDatabaseFixture fixture)
{
    [SqlServerFact]
    public async Task Unsubscribe_bearer_tokens_change_only_the_matching_users_preferences()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        string tokenA;
        string tokenB;

        await using (var ownerAScope = harness.AsOwnerA())
        {
            var preferences = new NotificationPreferencesService(
                ownerAScope.DbContext,
                ownerAScope.CurrentUser);
            await preferences.SetPreferencesAsync(new UpdateNotificationPreferencesRequest(
                ReminderEmailDelivery.Immediate,
                BirthdayRemindersEnabled: true,
                DefaultBirthdayLeadDays: 1));
            tokenA = (await preferences.GetPreferencesAsync()).UnsubscribeToken!;
        }

        await using (var ownerBScope = harness.AsOwnerB())
        {
            var preferences = new NotificationPreferencesService(
                ownerBScope.DbContext,
                ownerBScope.CurrentUser);
            await preferences.SetPreferencesAsync(new UpdateNotificationPreferencesRequest(
                ReminderEmailDelivery.DailyDigest,
                BirthdayRemindersEnabled: false,
                DefaultBirthdayLeadDays: 2));
            tokenB = (await preferences.GetPreferencesAsync()).UnsubscribeToken!;
        }

        await using (var anonymousScope = harness.As(null))
        {
            var unsubscribe = new UnsubscribeService(anonymousScope.DbContext);
            (await unsubscribe.UnsubscribeAsync("invalid-synthetic-token")).Should().BeFalse();
            (await unsubscribe.UnsubscribeAsync(" ")).Should().BeFalse();
            (await unsubscribe.UnsubscribeAsync(tokenB)).Should().BeTrue();
        }

        await using (var verify = fixture.CreateDbContext())
        {
            (await verify.UserProfiles.AsNoTracking()
                .SingleAsync(profile => profile.OwnerId == harness.OwnerA.Id))
                .ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.Immediate);
            (await verify.UserProfiles.AsNoTracking()
                .SingleAsync(profile => profile.OwnerId == harness.OwnerB.Id))
                .ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.None);
        }

        await using (var anonymousScope = harness.As(null))
        {
            (await new UnsubscribeService(anonymousScope.DbContext).UnsubscribeAsync(tokenA)).Should().BeTrue();
        }

        await using var final = fixture.CreateDbContext();
        (await final.UserProfiles.AsNoTracking()
            .SingleAsync(profile => profile.OwnerId == harness.OwnerA.Id))
            .ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.None);
        (await final.UserProfiles.AsNoTracking()
            .SingleAsync(profile => profile.OwnerId == harness.OwnerB.Id))
            .ReminderEmailDelivery.Should().Be(ReminderEmailDelivery.None);
    }

    [SqlServerFact]
    public async Task Trusted_scheduler_delivers_per_owner_and_stamps_due_reminders_once()
    {
        var harness = await SqlIsolationTestHarness.CreateAsync(fixture);
        var today = DateOnly.FromDateTime(harness.Clock.GetUtcNow().UtcDateTime);
        Guid reminderA;
        Guid reminderB;
        Guid archivedReminderA;

        await using (var setup = fixture.CreateDbContext())
        {
            var preferencesA = new NotificationPreferencesService(
                setup,
                new FakeCurrentUser(harness.OwnerA.Id));
            await preferencesA.SetPreferencesAsync(new UpdateNotificationPreferencesRequest(
                ReminderEmailDelivery.Immediate,
                BirthdayRemindersEnabled: true,
                DefaultBirthdayLeadDays: 0));

            var preferencesB = new NotificationPreferencesService(
                setup,
                new FakeCurrentUser(harness.OwnerB.Id));
            await preferencesB.SetPreferencesAsync(new UpdateNotificationPreferencesRequest(
                ReminderEmailDelivery.DailyDigest,
                BirthdayRemindersEnabled: true,
                DefaultBirthdayLeadDays: 0));

            var personA = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerA.Id, "Scheduler A");
            var personB = await TestDataFactory.CreatePersonAsync(setup, harness.OwnerB.Id, "Scheduler B");
            var archivedPersonA = await TestDataFactory.CreatePersonAsync(
                setup,
                harness.OwnerA.Id,
                "Archived scheduler A",
                isArchived: true);
            reminderA = (await new ReminderService(
                    setup,
                    new FakeCurrentUser(harness.OwnerA.Id),
                    new UserTimeZoneService(setup, new FakeCurrentUser(harness.OwnerA.Id), harness.Clock),
                    harness.Clock)
                .CreateAsync(new CreateReminderRequest
                {
                    PersonId = personA,
                    Title = "A due reminder",
                    DueDate = today,
                })).Id;
            reminderB = (await new ReminderService(
                    setup,
                    new FakeCurrentUser(harness.OwnerB.Id),
                    new UserTimeZoneService(setup, new FakeCurrentUser(harness.OwnerB.Id), harness.Clock),
                    harness.Clock)
                .CreateAsync(new CreateReminderRequest
                {
                    PersonId = personB,
                    Title = "B due reminder",
                    DueDate = today,
                })).Id;
            archivedReminderA = Guid.NewGuid();
            setup.Reminders.Add(new Reminder
            {
                Id = archivedReminderA,
                OwnerId = harness.OwnerA.Id,
                PersonId = archivedPersonA,
                Title = "Archived due reminder",
                DueDate = today,
            });
            await setup.SaveChangesAsync();
            setup.ChangeTracker.Clear();
        }

        var sender = new RecordingReminderEmailSender();
        await using var runnerContext = fixture.CreateDbContext();
        var runner = new ReminderSchedulerRunner(
            runnerContext,
            sender,
            harness.Clock,
            NullLogger<ReminderSchedulerRunner>.Instance);

        (await runner.RunDueRemindersJobAsync()).Should().BeGreaterThanOrEqualTo(2);

        sender.ImmediateDeliveries.Should().Contain(delivery =>
            delivery.Email == harness.OwnerA.Email && delivery.Title == "A due reminder");
        sender.Digests.Should().Contain(digest =>
            digest.Email == harness.OwnerB.Email
            && digest.Items.Any(item => item.Title == "B due reminder"));
        sender.ImmediateDeliveries.Should().NotContain(delivery => delivery.Title == "Archived due reminder");
        sender.Digests.Should().NotContain(digest =>
            digest.Items.Any(item => item.Title == "Archived due reminder"));

        await using (var verify = fixture.CreateDbContext())
        {
            var delivered = await verify.Reminders.AsNoTracking()
                .Where(reminder => reminder.Id == reminderA || reminder.Id == reminderB)
                .ToListAsync();
            delivered.Should().HaveCount(2);
            delivered.Should().OnlyContain(reminder => reminder.LastDeliveredDate == today);
            (await verify.Reminders.AsNoTracking()
                .SingleAsync(reminder => reminder.Id == archivedReminderA))
                .LastDeliveredDate.Should().BeNull();
        }

        var deliveryCount = sender.DeliveryCount;
        (await runner.RunDueRemindersJobAsync()).Should().Be(0);
        sender.DeliveryCount.Should().Be(deliveryCount);
    }

    private sealed class RecordingReminderEmailSender : IReminderEmailSender
    {
        public List<ImmediateDelivery> ImmediateDeliveries { get; } = [];

        public List<DigestDelivery> Digests { get; } = [];

        public int DeliveryCount => ImmediateDeliveries.Count + Digests.Count;

        public Task SendImmediateReminderAsync(
            string toEmail,
            string personName,
            string reminderTitle,
            DateOnly dueDate,
            string? unsubscribeToken,
            CancellationToken cancellationToken = default)
        {
            ImmediateDeliveries.Add(new ImmediateDelivery(toEmail, personName, reminderTitle, dueDate));
            return Task.CompletedTask;
        }

        public Task SendDailyDigestAsync(
            string toEmail,
            IReadOnlyList<DigestReminderItem> items,
            string? unsubscribeToken,
            CancellationToken cancellationToken = default)
        {
            Digests.Add(new DigestDelivery(toEmail, items.ToArray()));
            return Task.CompletedTask;
        }
    }

    private sealed record ImmediateDelivery(string Email, string PersonName, string Title, DateOnly DueDate);

    private sealed record DigestDelivery(string Email, IReadOnlyList<DigestReminderItem> Items);
}
