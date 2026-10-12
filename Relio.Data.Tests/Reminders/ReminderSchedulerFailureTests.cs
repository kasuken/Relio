using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Relio.Data.Reminders;
using Relio.Domain;
using static Relio.Data.Tests.Reminders.ReminderSchedulerTestData;

namespace Relio.Data.Tests.Reminders;

/// <summary>
/// The failure windows of reminder delivery (issue #121): a send or a save failing at any point
/// must never produce a duplicate email, and a failed send must be retried.
/// </summary>
public class ReminderSchedulerFailureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 7);

    [Fact]
    public async Task Send_failure_releases_the_claim_and_the_next_run_delivers_once()
    {
        var (database, clock, sender, reminders) = await ArrangeAsync(ReminderEmailDelivery.Immediate, ("Call", Today));
        sender.FailWhen = (_, call) => call == 1 ? new InvalidOperationException("smtp down") : null;

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);

        (await LedgerAsync(database, clock)).Should().BeEmpty();
        (await GetReminderAsync(database, clock, reminders[0].Id)).LastDeliveredDate.Should().BeNull();
        sender.ImmediateEmails.Should().BeEmpty();

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(1);

        sender.ImmediateEmails.Should().ContainSingle();
        (await LedgerAsync(database, clock)).Should().ContainSingle().Which.Status.Should().Be(ReminderDeliveryStatus.Sent);
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);
    }

    [Fact]
    public async Task Digest_send_failure_releases_every_item_and_the_retry_sends_one_digest()
    {
        var (database, clock, sender, reminders) = await ArrangeAsync(
            ReminderEmailDelivery.DailyDigest, ("First", new DateOnly(2026, 10, 6)), ("Second", Today));
        sender.FailWhen = (_, call) => call == 1 ? new InvalidOperationException("smtp down") : null;

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);

        (await LedgerAsync(database, clock)).Should().BeEmpty();
        foreach (var reminder in reminders)
        {
            (await GetReminderAsync(database, clock, reminder.Id)).LastDeliveredDate.Should().BeNull();
        }

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(2);

        sender.DigestEmails.Should().ContainSingle().Which.Items.Should().HaveCount(2);
        (await LedgerAsync(database, clock)).Should().HaveCount(2)
            .And.AllSatisfy(row => row.Status.Should().Be(ReminderDeliveryStatus.Sent));
    }

    [Fact]
    public async Task Partial_immediate_batch_failure_keeps_sent_items_and_retries_only_the_rest()
    {
        var (database, clock, sender, reminders) = await ArrangeAsync(
            ReminderEmailDelivery.Immediate,
            ("A", new DateOnly(2026, 10, 5)),
            ("B", new DateOnly(2026, 10, 6)),
            ("C", Today));
        sender.FailWhen = (_, call) => call == 2 ? new InvalidOperationException("smtp down") : null;

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(1);

        var ledger = await LedgerAsync(database, clock);
        ledger.Should().ContainSingle().Which.SourceId.Should().Be(reminders[0].Id);
        ledger[0].Status.Should().Be(ReminderDeliveryStatus.Sent);
        (await GetReminderAsync(database, clock, reminders[1].Id)).LastDeliveredDate.Should().BeNull();
        (await GetReminderAsync(database, clock, reminders[2].Id)).LastDeliveredDate.Should().BeNull();

        sender.FailWhen = null;
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(2);

        sender.ImmediateEmails.Select(e => e.ReminderTitle).Should().Equal("A", "B", "C");
        (await LedgerAsync(database, clock)).Should().HaveCount(3);
    }

    [Fact]
    public async Task Finalize_save_failure_after_sending_never_resends_after_restart()
    {
        var (database, clock, sender, _) = await ArrangeAsync(ReminderEmailDelivery.Immediate, ("Call", Today));
        var fault = new ReminderDeliveryFaultInterceptor(DeliverySave.Finalize, FaultTiming.BeforeSave);

        (await RunOnNewContextAsync(database, clock, sender, CancellationToken.None, fault)).Should().Be(1);

        fault.Fired.Should().BeTrue();
        (await LedgerAsync(database, clock)).Should().ContainSingle().Which.Status.Should().Be(ReminderDeliveryStatus.Pending);

        // A restart: a new context and a new runner.
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);
        sender.ImmediateEmails.Should().ContainSingle();
    }

    [Fact]
    public async Task Crash_after_claim_commit_and_before_send_is_at_most_once()
    {
        var (database, clock, sender, _) = await ArrangeAsync(ReminderEmailDelivery.Immediate, ("Call", Today));
        var fault = new ReminderDeliveryFaultInterceptor(DeliverySave.Claim, FaultTiming.AfterCommit);

        (await RunOnNewContextAsync(database, clock, sender, CancellationToken.None, fault)).Should().Be(0);

        fault.Fired.Should().BeTrue();
        sender.CallCount.Should().Be(0);

        // The claim survived the crash, so the restart sends nothing: one lost email, never two.
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);
        sender.CallCount.Should().Be(0);
        (await LedgerAsync(database, clock)).Should().ContainSingle().Which.Status.Should().Be(ReminderDeliveryStatus.Pending);
    }

    [Fact]
    public async Task Claim_save_failure_sends_nothing_and_the_next_run_delivers_once()
    {
        var (database, clock, sender, reminders) = await ArrangeAsync(ReminderEmailDelivery.Immediate, ("Call", Today));
        var fault = new ReminderDeliveryFaultInterceptor(DeliverySave.Claim, FaultTiming.BeforeSave);

        (await RunOnNewContextAsync(database, clock, sender, CancellationToken.None, fault)).Should().Be(0);

        fault.Fired.Should().BeTrue();
        sender.CallCount.Should().Be(0);
        (await LedgerAsync(database, clock)).Should().BeEmpty();
        (await GetReminderAsync(database, clock, reminders[0].Id)).LastDeliveredDate.Should().BeNull();

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(1);
        sender.ImmediateEmails.Should().ContainSingle();
    }

    [Fact]
    public async Task Cancellation_between_sends_releases_unattempted_claims()
    {
        var (database, clock, sender, reminders) = await ArrangeAsync(
            ReminderEmailDelivery.Immediate, ("First", new DateOnly(2026, 10, 6)), ("Second", Today));
        using var cts = new CancellationTokenSource();
        sender.OnSend = call =>
        {
            if (call == 1)
            {
                cts.Cancel();
            }
        };

        var act = () => RunOnNewContextAsync(database, clock, sender, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        var ledger = await LedgerAsync(database, clock);
        ledger.Should().ContainSingle().Which.SourceId.Should().Be(reminders[0].Id);
        ledger[0].Status.Should().Be(ReminderDeliveryStatus.Sent);
        (await GetReminderAsync(database, clock, reminders[1].Id)).LastDeliveredDate.Should().BeNull();

        sender.OnSend = null;
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(1);

        sender.ImmediateEmails.Select(e => e.ReminderTitle).Should().Equal("First", "Second");
    }

    [Fact]
    public async Task Cancellation_before_a_digest_send_releases_every_item()
    {
        var (database, clock, sender, reminders) = await ArrangeAsync(
            ReminderEmailDelivery.DailyDigest, ("First", new DateOnly(2026, 10, 6)), ("Second", Today));
        using var cts = new CancellationTokenSource();
        await using (var dbContext = CreateDbContext(database, clock, new CancelOnClaimInterceptor(cts)))
        {
            var act = () => CreateRunner(dbContext, sender, clock).RunDueRemindersJobAsync(cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        sender.CallCount.Should().Be(0);
        (await LedgerAsync(database, clock)).Should().BeEmpty();
        (await GetReminderAsync(database, clock, reminders[0].Id)).LastDeliveredDate.Should().BeNull();

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(2);
    }

    [Fact]
    public async Task Birthday_send_failure_releases_the_claim_and_the_retry_delivers_once()
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(Now);
        var sender = new FakeReminderEmailSender();
        await SeedOwnerAsync(database, clock, "owner", ReminderEmailDelivery.Immediate);
        await AddPersonAsync(database, clock, "owner", "Ada", null, p =>
        {
            p.BirthdayMonth = 10;
            p.BirthdayDay = 7;
        });
        sender.FailWhen = (_, call) => call == 1 ? new InvalidOperationException("smtp down") : null;

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);
        (await LedgerAsync(database, clock)).Should().BeEmpty();

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(1);
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);
        sender.ImmediateEmails.Should().ContainSingle().Which.ReminderTitle.Should().Be("Birthday");
    }

    [Fact]
    public async Task A_reminder_whose_ledger_row_was_purged_is_not_resent()
    {
        var (database, clock, sender, _) = await ArrangeAsync(ReminderEmailDelivery.Immediate, ("Call", Today));

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(1);

        // Well past retention the row is purged; the stamp on the reminder still guards it.
        clock.Advance(TimeSpan.FromDays(ReminderSchedulerRunner.DeliveryRetentionDays + 5));
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(0);

        (await LedgerAsync(database, clock)).Should().BeEmpty();
        sender.ImmediateEmails.Should().ContainSingle();
    }

    private sealed class CancelOnClaimInterceptor(CancellationTokenSource cts)
        : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            cts.Cancel();
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task One_owners_send_failure_does_not_block_other_owners()
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(Now);
        var sender = new FakeReminderEmailSender();
        foreach (var owner in new[] { "owner-a", "owner-b" })
        {
            await SeedOwnerAsync(database, clock, owner, ReminderEmailDelivery.Immediate);
            var person = await AddPersonAsync(database, clock, owner, "Ada");
            await AddReminderAsync(database, clock, owner, person.Id, $"Call {owner}", Today);
        }

        sender.FailWhen = (email, _) => email == EmailOf("owner-a") ? new InvalidOperationException("smtp down") : null;

        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(1);
        sender.ImmediateEmails.Should().ContainSingle().Which.ToEmail.Should().Be(EmailOf("owner-b"));

        sender.FailWhen = null;
        (await RunOnNewContextAsync(database, clock, sender)).Should().Be(1);

        sender.ImmediateEmails.Select(e => e.ToEmail).Should()
            .BeEquivalentTo([EmailOf("owner-b"), EmailOf("owner-a")]);
    }

    [Fact]
    public async Task Failure_logs_contain_only_ids_and_exception_types()
    {
        const string sentinel = "SENTINEL-private-title";
        var (database, clock, sender, _) = await ArrangeAsync(ReminderEmailDelivery.Immediate, (sentinel, Today));
        sender.FailWhen = (_, _) => new InvalidOperationException($"smtp rejected {sentinel} for {EmailOf("owner")}");
        var logger = new CapturingLogger<ReminderSchedulerRunner>();

        await using (var dbContext = CreateDbContext(database, clock))
        {
            await CreateRunner(dbContext, sender, clock, logger).RunDueRemindersJobAsync();
        }

        var fault = new ReminderDeliveryFaultInterceptor(DeliverySave.Finalize, FaultTiming.BeforeSave);
        sender.FailWhen = null;
        await using (var dbContext = CreateDbContext(database, clock, fault))
        {
            await CreateRunner(dbContext, sender, clock, logger).RunDueRemindersJobAsync();
        }

        logger.Entries.Should().Contain(e => e.Message.Contains("InvalidOperationException"));
        logger.Entries.Should().NotBeEmpty().And.AllSatisfy(e =>
        {
            e.Exception.Should().BeNull();
            e.Message.Should().NotContain(sentinel);
            e.Message.Should().NotContain("example.com");
            e.Message.Should().NotContain("Ada");
        });
    }

    private static async Task<(
        DbContextOptions<RelioDbContext> Database,
        FakeTimeProvider Clock,
        FakeReminderEmailSender Sender,
        List<Reminder> Reminders)> ArrangeAsync(
        ReminderEmailDelivery delivery,
        params (string Title, DateOnly Due)[] reminders)
    {
        var database = NewDatabase();
        var clock = new FakeTimeProvider(Now);
        await SeedOwnerAsync(database, clock, "owner", delivery);
        var person = await AddPersonAsync(database, clock, "owner", "Ada");
        var created = new List<Reminder>();
        foreach (var (title, due) in reminders)
        {
            created.Add(await AddReminderAsync(database, clock, "owner", person.Id, title, due));
        }

        return (database, clock, new FakeReminderEmailSender(), created);
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }
}
