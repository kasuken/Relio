using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Relio.Application.Reminders;
using Relio.Data.Reminders;
using Relio.Domain;
using static Relio.Data.Tests.Reminders.ReminderSchedulerTestData;

namespace Relio.Data.Tests.Reminders;

/// <summary>
/// Birthday emails and the calendar edge cases of delivery (issue #121): preferences, lead days,
/// leap-day birthdays, time zones around midnight, DST, purge and privacy.
/// </summary>
public class ReminderSchedulerBirthdayTests
{
    private static readonly DateTimeOffset Oct7 = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Birthday_on_the_day_is_emailed_once_with_name_birthday_title_and_date()
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(Oct7);
        var sender = new FakeReminderEmailSender();
        await SeedOwnerAsync(database, clock, "owner", ReminderEmailDelivery.Immediate);
        var person = await AddPersonAsync(database, clock, "owner", "Grace", "Hopper", BirthdayOn(10, 7, 1990));

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(1);
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);

        var email = sender.ImmediateEmails.Should().ContainSingle().Subject;
        email.PersonName.Should().Be("Grace Hopper");
        email.ReminderTitle.Should().Be(ReminderEmailContent.BirthdayTitle).And.Be("Birthday");
        email.DueDate.Should().Be(new DateOnly(2026, 10, 7));

        var row = (await LedgerAsync(database, clock)).Should().ContainSingle().Subject;
        row.Kind.Should().Be(ReminderDeliveryKind.Birthday);
        row.SourceId.Should().Be(person.Id);
        row.OccurrenceDate.Should().Be(new DateOnly(2026, 10, 7));
        row.Status.Should().Be(ReminderDeliveryStatus.Sent);
    }

    [Fact]
    public async Task Birthday_is_included_in_the_daily_digest_sorted_with_due_reminders()
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(Oct7);
        var sender = new FakeReminderEmailSender();
        await SeedOwnerAsync(database, clock, "owner", ReminderEmailDelivery.DailyDigest);
        var person = await AddPersonAsync(database, clock, "owner", "Grace", "Hopper", BirthdayOn(10, 7, null));
        await AddReminderAsync(database, clock, "owner", person.Id, "Earlier", new DateOnly(2026, 10, 6));
        await AddReminderAsync(database, clock, "owner", person.Id, "Today", new DateOnly(2026, 10, 7));

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(3);

        var digest = sender.DigestEmails.Should().ContainSingle().Subject;
        digest.Items.Select(i => i.Title).Should().Equal("Earlier", "Today", "Birthday");
        digest.Items.Select(i => i.DueDate).Should().Equal(
            new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 7));
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);
    }

    [Fact]
    public async Task Person_lead_days_override_the_profile_default_and_open_the_window_once()
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero));
        var sender = new FakeReminderEmailSender();
        await SeedOwnerAsync(database, clock, "owner", ReminderEmailDelivery.Immediate, defaultBirthdayLeadDays: 1);
        await AddPersonAsync(database, clock, "owner", "Early", null, p =>
        {
            BirthdayOn(10, 10, null)(p);
            p.BirthdayReminderLeadDays = 3;
        });
        await AddPersonAsync(database, clock, "owner", "Default", null, BirthdayOn(10, 9, null));

        var sentPerDay = new List<int>();
        for (var day = 0; day < 5; day++)
        {
            sentPerDay.Add(await RunOnNewContextAsync(database, clock, sender));
            clock.Advance(TimeSpan.FromDays(1));
        }

        // Oct 6, 7, 8, 9, 10: "Early" opens on Oct 7 (3 days ahead), "Default" on Oct 8 (1 day ahead).
        sentPerDay.Should().Equal(0, 1, 1, 0, 0);
        sender.ImmediateEmails.Select(e => e.PersonName).Should().Equal("Early", "Default");
        sender.ImmediateEmails.Select(e => e.DueDate).Should().Equal(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 9));
    }

    [Fact]
    public async Task Changing_lead_days_after_delivery_does_not_resend_the_same_birthday()
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));
        var sender = new FakeReminderEmailSender();
        await SeedOwnerAsync(database, clock, "owner", ReminderEmailDelivery.Immediate);
        var person = await AddPersonAsync(database, clock, "owner", "Ada", null, p =>
        {
            BirthdayOn(10, 10, null)(p);
            p.BirthdayReminderLeadDays = 3;
        });

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(1);

        await UpdatePersonAsync(database, clock, person.Id, p => p.BirthdayReminderLeadDays = 5);
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);

        await UpdatePersonAsync(database, clock, person.Id, p => p.BirthdayReminderLeadDays = 1);
        clock.Advance(TimeSpan.FromDays(3));
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);

        sender.ImmediateEmails.Should().ContainSingle();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Birthdays_are_not_emailed_when_globally_or_per_person_disabled(bool globallyDisabled, bool personDisabled)
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(Oct7);
        var sender = new FakeReminderEmailSender();
        await SeedOwnerAsync(
            database, clock, "owner", ReminderEmailDelivery.Immediate, birthdayRemindersEnabled: !globallyDisabled);
        await AddPersonAsync(database, clock, "owner", "Ada", null, p =>
        {
            BirthdayOn(10, 7, null)(p);
            p.BirthdayReminderDisabled = personDisabled;
        });

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);

        sender.CallCount.Should().Be(0);
        (await LedgerAsync(database, clock)).Should().BeEmpty();
    }

    [Fact]
    public async Task Archived_people_birthdays_are_not_emailed()
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(Oct7);
        var sender = new FakeReminderEmailSender();
        await SeedOwnerAsync(database, clock, "owner", ReminderEmailDelivery.Immediate);
        await AddPersonAsync(database, clock, "owner", "Ada", null, p =>
        {
            BirthdayOn(10, 7, null)(p);
            p.IsArchived = true;
        });

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);

        sender.CallCount.Should().Be(0);
        (await LedgerAsync(database, clock)).Should().BeEmpty();
    }

    [Fact]
    public async Task Turning_email_on_mid_window_sends_the_birthday_once()
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.Zero));
        var sender = new FakeReminderEmailSender();
        await SeedOwnerAsync(database, clock, "owner", ReminderEmailDelivery.None, defaultBirthdayLeadDays: 3);
        await AddPersonAsync(database, clock, "owner", "Ada", null, BirthdayOn(10, 10, null));

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);

        await UpdateProfileAsync(database, clock, "owner", p => p.ReminderEmailDelivery = ReminderEmailDelivery.Immediate);

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(1);
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);
        sender.ImmediateEmails.Should().ContainSingle().Which.DueDate.Should().Be(new DateOnly(2026, 10, 10));
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(null)]
    public async Task Feb_29_birthday_is_observed_on_Feb_28_in_non_leap_years_and_Feb_29_in_leap_years(int? year)
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(new DateTimeOffset(2027, 2, 28, 10, 0, 0, TimeSpan.Zero));
        var sender = new FakeReminderEmailSender();
        await SeedOwnerAsync(database, clock, "owner", ReminderEmailDelivery.Immediate);
        await AddPersonAsync(database, clock, "owner", "Leap", null, BirthdayOn(2, 29, year));

        // 2027 is not a leap year: Feb 28 is the day.
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(1);
        sender.ImmediateEmails[0].DueDate.Should().Be(new DateOnly(2027, 2, 28));

        clock.SetUtcNow(new DateTimeOffset(2027, 3, 1, 10, 0, 0, TimeSpan.Zero));
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);

        // 2028 is a leap year: Feb 28 is nothing, Feb 29 is the day.
        clock.SetUtcNow(new DateTimeOffset(2028, 2, 28, 10, 0, 0, TimeSpan.Zero));
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);

        clock.SetUtcNow(new DateTimeOffset(2028, 2, 29, 10, 0, 0, TimeSpan.Zero));
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(1);
        sender.ImmediateEmails[1].DueDate.Should().Be(new DateOnly(2028, 2, 29));
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);
    }

    [Theory]
    [InlineData("Pacific/Kiritimati", 8, "2026-10-07T09:59:00Z", "2026-10-07T10:00:00Z")]
    [InlineData("Pacific/Pago_Pago", 7, "2026-10-07T10:59:00Z", "2026-10-07T11:00:00Z")]
    public async Task Birthday_follows_the_users_midnight_in_Kiritimati_and_Pago_Pago(
        string timeZoneId,
        int birthdayDay,
        string beforeMidnightUtc,
        string atMidnightUtc)
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse(beforeMidnightUtc, CultureInfo.InvariantCulture));
        var sender = new FakeReminderEmailSender();
        await SeedOwnerAsync(database, clock, "owner", ReminderEmailDelivery.Immediate, timeZoneId);
        await AddPersonAsync(database, clock, "owner", "Ada", null, BirthdayOn(10, birthdayDay, null));

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);

        clock.SetUtcNow(DateTimeOffset.Parse(atMidnightUtc, CultureInfo.InvariantCulture));
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(1);
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);

        sender.ImmediateEmails.Should().ContainSingle().Which.DueDate.Should().Be(new DateOnly(2026, 10, birthdayDay));
    }

    [Theory]
    [InlineData("Europe/Rome", "2026-10-25T00:30:00Z", "2026-10-25T01:30:00Z", 10, 25)]
    [InlineData("America/New_York", "2026-03-08T06:30:00Z", "2026-03-08T07:30:00Z", 3, 8)]
    public async Task Dst_transitions_do_not_deliver_twice(
        string timeZoneId,
        string firstInstantUtc,
        string secondInstantUtc,
        int month,
        int day)
    {
        // Rome falls back at 01:00 UTC (02:30 local happens twice: 00:30Z and 01:30Z); New York
        // springs forward at 07:00 UTC (the 02:00 hour is skipped between 06:30Z and 07:30Z).
        var database = NewDatabase();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse(firstInstantUtc, CultureInfo.InvariantCulture));
        var sender = new FakeReminderEmailSender();
        await SeedOwnerAsync(database, clock, "owner", ReminderEmailDelivery.Immediate, timeZoneId);
        var person = await AddPersonAsync(database, clock, "owner", "Ada", null, BirthdayOn(month, day, null));
        await AddReminderAsync(database, clock, "owner", person.Id, "Call", new DateOnly(2026, month, day));

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(2);

        clock.SetUtcNow(DateTimeOffset.Parse(secondInstantUtc, CultureInfo.InvariantCulture));
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);

        sender.ImmediateEmails.Select(e => e.ReminderTitle).Should().BeEquivalentTo(["Call", "Birthday"]);
        (await LedgerAsync(database, clock)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Next_years_birthday_is_emailed_again()
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(Oct7);
        var sender = new FakeReminderEmailSender();
        await SeedOwnerAsync(database, clock, "owner", ReminderEmailDelivery.Immediate);
        await AddPersonAsync(database, clock, "owner", "Ada", null, BirthdayOn(10, 7, 1990));

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(1);

        clock.SetUtcNow(new DateTimeOffset(2027, 10, 7, 10, 0, 0, TimeSpan.Zero));
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(1);
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);

        sender.ImmediateEmails.Select(e => e.DueDate).Should().Equal(new DateOnly(2026, 10, 7), new DateOnly(2027, 10, 7));
        sender.ImmediateEmails.Should().AllSatisfy(e => e.ReminderTitle.Should().Be("Birthday"));
    }

    [Fact]
    public async Task Emails_never_contain_notes_difficult_moments_or_profile_narratives()
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(Oct7);
        var sender = new FakeReminderEmailSender();
        string[] sentinels =
        [
            "SENTINEL-note", "SENTINEL-interaction", "SENTINEL-moment-description", "SENTINEL-moment-trigger",
            "SENTINEL-moment-resolution", "SENTINEL-moment-lesson", "SENTINEL-how-we-met", "SENTINEL-details",
            "SENTINEL-nickname",
        ];

        foreach (var (owner, delivery) in new[]
        {
            ("owner-immediate", ReminderEmailDelivery.Immediate),
            ("owner-digest", ReminderEmailDelivery.DailyDigest),
        })
        {
            await SeedOwnerAsync(database, clock, owner, delivery);
            var person = await AddPersonAsync(database, clock, owner, "Grace", "Hopper", p =>
            {
                BirthdayOn(10, 7, 1906)(p);
                p.Nickname = "SENTINEL-nickname";
                p.HowWeMet = "SENTINEL-how-we-met";
                p.Details = "SENTINEL-details";
            });
            await AddReminderAsync(database, clock, owner, person.Id, "Call Grace", new DateOnly(2026, 10, 7));

            await using var dbContext = CreateDbContext(database, clock);
            dbContext.Notes.Add(new Note { OwnerId = owner, PersonId = person.Id, Text = "SENTINEL-note" });
            var interaction = new Interaction
            {
                OwnerId = owner,
                OccurredOn = new DateOnly(2026, 10, 1),
                Kind = InteractionKind.Call,
                Description = "SENTINEL-interaction",
            };
            dbContext.Interactions.Add(interaction);
            dbContext.InteractionParticipants.Add(new InteractionParticipant
            {
                OwnerId = owner,
                InteractionId = interaction.Id,
                PersonId = person.Id,
            });
            dbContext.DifficultMoments.Add(new DifficultMoment
            {
                OwnerId = owner,
                PersonId = person.Id,
                OccurredOn = new DateOnly(2026, 9, 1),
                Description = "SENTINEL-moment-description",
                Trigger = "SENTINEL-moment-trigger",
                Resolution = "SENTINEL-moment-resolution",
                LessonsLearned = "SENTINEL-moment-lesson",
            });
            await dbContext.SaveChangesAsync();
        }

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(4);

        sender.ImmediateEmails.Should().HaveCount(2);
        sender.DigestEmails.Should().ContainSingle();
        var recorded = sender.AllRecordedText.ToList();
        recorded.Should().NotBeEmpty();
        foreach (var sentinel in sentinels)
        {
            recorded.Should().NotContain(text => text.Contains(sentinel, StringComparison.Ordinal));
        }

        sender.ImmediateEmails.Select(e => e.ReminderTitle).Should().BeEquivalentTo(["Call Grace", "Birthday"]);
        sender.ImmediateEmails.Should().AllSatisfy(e => e.PersonName.Should().Be("Grace Hopper"));
        recorded.Should().NotContain(text => text.Contains("1906", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Purge_removes_only_expired_delivery_rows()
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(Oct7);
        var sender = new FakeReminderEmailSender();
        var expired = Row(claimed: new DateTime(2026, 9, 1), occurrence: new DateOnly(2026, 9, 1));
        var upcomingBirthday = Row(claimed: new DateTime(2026, 9, 1), occurrence: new DateOnly(2026, 10, 20));
        var occurrenceStillRecent = Row(claimed: new DateTime(2026, 9, 1), occurrence: new DateOnly(2026, 10, 6));
        var claimedRecently = Row(claimed: new DateTime(2026, 10, 6), occurrence: new DateOnly(2026, 10, 1));
        await using (var dbContext = CreateDbContext(database, clock))
        {
            dbContext.ReminderDeliveries.AddRange(expired, upcomingBirthday, occurrenceStillRecent, claimedRecently);
            await dbContext.SaveChangesAsync();
        }

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);

        (await LedgerAsync(database, clock)).Select(r => r.Id).Should()
            .BeEquivalentTo([upcomingBirthday.Id, occurrenceStillRecent.Id, claimedRecently.Id]);
    }

    [Fact]
    public async Task Purge_removes_at_most_one_batch_per_run()
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(Oct7);
        var sender = new FakeReminderEmailSender();
        await using (var dbContext = CreateDbContext(database, clock))
        {
            for (var i = 0; i < ReminderSchedulerRunner.PurgeBatchSize + 1; i++)
            {
                dbContext.ReminderDeliveries.Add(Row(new DateTime(2026, 9, 1), new DateOnly(2026, 9, 1)));
            }

            await dbContext.SaveChangesAsync();
        }

        await RunOnNewContextAsync(database, clock, sender);
        (await LedgerAsync(database, clock)).Should().ContainSingle();

        await RunOnNewContextAsync(database, clock, sender);
        (await LedgerAsync(database, clock)).Should().BeEmpty();
    }

    private static Action<Person> BirthdayOn(int month, int day, int? year) => person =>
    {
        person.BirthdayMonth = month;
        person.BirthdayDay = day;
        person.BirthdayYear = year;
    };

    private static ReminderDelivery Row(DateTime claimed, DateOnly occurrence) => new()
    {
        UserId = "owner",
        Kind = ReminderDeliveryKind.Birthday,
        SourceId = Guid.NewGuid(),
        OccurrenceDate = occurrence,
        DeliveryDate = occurrence,
        Status = ReminderDeliveryStatus.Sent,
        ClaimedAtUtc = claimed,
        SentAtUtc = claimed,
    };
}
