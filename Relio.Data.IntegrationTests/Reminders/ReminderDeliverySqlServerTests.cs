using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Relio.Application.Reminders;
using Relio.Data.Configurations;
using Relio.Data.Identity;
using Relio.Data.IntegrationTests.Infrastructure;
using Relio.Data.IntegrationTests.Isolation;
using Relio.Data.Reminders;
using Relio.Domain;

namespace Relio.Data.IntegrationTests.Reminders;

/// <summary>
/// Proves reminder and birthday delivery (issue #121) against the real SQL Server migrations: the
/// unique claim ledger, a competing worker losing its claim, parallel workers, send and save
/// failures, restarts, query translation and the retention purge. Each test gets its own migrated
/// database because the runner sweeps every owner, so rows from other tests would be noise.
/// </summary>
public sealed class ReminderDeliverySqlServerTests
{
    private static readonly DateTimeOffset Oct7Noon = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 7);

    [SqlServerFact]
    public async Task Migration_creates_a_unique_delivery_key_and_a_no_action_owner_key()
    {
        await using var database = await ReminderDatabase.CreateAsync();
        var ownerId = await database.AddOwnerAsync(ReminderEmailDelivery.Immediate);
        var sourceId = Guid.NewGuid();

        await using (var first = database.CreateDbContext(new FixedTimeProvider(Oct7Noon)))
        {
            first.ReminderDeliveries.Add(NewRow(ownerId, sourceId));
            await first.SaveChangesAsync();
        }

        await using (var duplicate = database.CreateDbContext(new FixedTimeProvider(Oct7Noon)))
        {
            duplicate.ReminderDeliveries.Add(NewRow(ownerId, sourceId));
            var failure = await Record.ExceptionAsync(() => duplicate.SaveChangesAsync());

            var sql = failure.Should().BeOfType<DbUpdateException>().Which.InnerException.Should().BeOfType<SqlException>().Subject;
            sql.Number.Should().BeOneOf(2601, 2627);
            sql.Message.Should().Contain(ReminderDeliveryConfiguration.DeliveryKeyIndexName);
        }

        await using (var unknownUser = database.CreateDbContext(new FixedTimeProvider(Oct7Noon)))
        {
            unknownUser.ReminderDeliveries.Add(NewRow($"missing-{Guid.NewGuid():N}", Guid.NewGuid()));
            var failure = await Record.ExceptionAsync(() => unknownUser.SaveChangesAsync());

            var sql = failure.Should().BeOfType<DbUpdateException>().Which.InnerException.Should().BeOfType<SqlException>().Subject;
            sql.Number.Should().Be(547);
            sql.Message.Should().Contain("FK_ReminderDeliveries_AspNetUsers_UserId");
        }

        await using var raw = database.CreateDbContext(new FixedTimeProvider(Oct7Noon));
        var checkFailure = await Record.ExceptionAsync(() => raw.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO ReminderDeliveries
                (Id, UserId, Kind, SourceId, OccurrenceDate, DeliveryDate, Status, ClaimedAtUtc)
            VALUES ({Guid.NewGuid()}, {ownerId}, N'Other', {Guid.NewGuid()}, '2026-10-07', '2026-10-07', N'Pending', SYSUTCDATETIME())
            """));

        var checkSql = checkFailure.Should().BeOfType<SqlException>().Subject;
        checkSql.Number.Should().Be(547);
        checkSql.Message.Should().Contain("CK_ReminderDeliveries_Kind");
    }

    [SqlServerFact]
    public async Task A_competing_worker_that_claims_first_wins_and_the_loser_sends_nothing()
    {
        await using var database = await ReminderDatabase.CreateAsync();
        var clock = new FixedTimeProvider(Oct7Noon);
        var ownerA = await database.AddOwnerAsync(ReminderEmailDelivery.Immediate);
        var ownerB = await database.AddOwnerAsync(ReminderEmailDelivery.DailyDigest);
        var reminderIds = new List<Guid>();
        foreach (var owner in new[] { ownerA, ownerB })
        {
            var person = await database.AddPersonAsync(owner, "Ada", p =>
            {
                p.BirthdayMonth = 10;
                p.BirthdayDay = 7;
            });
            reminderIds.Add(await database.AddReminderAsync(owner, person, $"First {owner}", Today));
            reminderIds.Add(await database.AddReminderAsync(owner, person, $"Second {owner}", Today.AddDays(-1)));
        }

        var winnerSender = new RecordingReminderEmailSender();
        var loserSender = new RecordingReminderEmailSender();
        var interceptor = new RunBeforeDeliveryClaimInterceptor(ownerA, async ct =>
        {
            await using var winnerContext = database.CreateDbContext(clock);
            await ReminderDatabase.CreateRunner(winnerContext, winnerSender, clock).RunDueRemindersJobAsync(ct);
        });

        await using (var loserContext = database.CreateDbContext(clock, interceptor))
        {
            await ReminderDatabase.CreateRunner(loserContext, loserSender, clock).RunDueRemindersJobAsync();
        }

        interceptor.Fired.Should().BeTrue();
        var emailA = database.EmailOf(ownerA);
        var emailB = database.EmailOf(ownerB);

        // A: the winner sent all three items once; the loser sent nothing for A.
        winnerSender.DeliveriesFor(emailA).Select(d => d.Title).Should()
            .BeEquivalentTo([$"First {ownerA}", $"Second {ownerA}", "Birthday"]);
        loserSender.DeliveriesFor(emailA).Should().BeEmpty();

        // B (never contested by the interceptor): exactly one digest in total, from whoever got there first.
        (winnerSender.DigestCountFor(emailB) + loserSender.DigestCountFor(emailB)).Should().Be(1);
        (winnerSender.DeliveriesFor(emailB).Count + loserSender.DeliveriesFor(emailB).Count).Should().Be(3);

        await using var verify = database.CreateDbContext(clock);
        var ledger = await verify.ReminderDeliveries.AsNoTracking().ToListAsync();
        ledger.Where(r => r.UserId == ownerA).Should().HaveCount(3);
        ledger.Where(r => r.UserId == ownerB).Should().HaveCount(3);
        ledger.Select(r => (r.UserId, r.Kind, r.SourceId, r.OccurrenceDate)).Should().OnlyHaveUniqueItems();
        ledger.Should().AllSatisfy(r => r.Status.Should().Be(ReminderDeliveryStatus.Sent));
        var reminders = await verify.Reminders.AsNoTracking().Where(r => reminderIds.Contains(r.Id)).ToListAsync();
        reminders.Should().HaveCount(4).And.AllSatisfy(r => r.LastDeliveredDate.Should().Be(Today));
    }

    [SqlServerFact]
    public async Task A_conflict_on_one_item_rolls_back_the_whole_claim_and_the_rest_is_delivered_next_run()
    {
        await using var database = await ReminderDatabase.CreateAsync();
        var clock = new FixedTimeProvider(Oct7Noon);
        var owner = await database.AddOwnerAsync(ReminderEmailDelivery.Immediate, birthdays: false);
        var person = await database.AddPersonAsync(owner, "Ada");
        var first = await database.AddReminderAsync(owner, person, "First", Today.AddDays(-1));
        var second = await database.AddReminderAsync(owner, person, "Second", Today);

        // Another claimant takes only the first reminder, between our read and our claim.
        var interceptor = new RunBeforeDeliveryClaimInterceptor(owner, async ct =>
        {
            await using var other = database.CreateDbContext(clock);
            other.ReminderDeliveries.Add(NewRow(owner, first, Today.AddDays(-1)));
            await other.SaveChangesAsync(ct);
        });
        var loserSender = new RecordingReminderEmailSender();
        await using (var loserContext = database.CreateDbContext(clock, interceptor))
        {
            (await ReminderDatabase.CreateRunner(loserContext, loserSender, clock).RunDueRemindersJobAsync())
                .Should().Be(0);
        }

        loserSender.CallCount.Should().Be(0);
        await using (var verify = database.CreateDbContext(clock))
        {
            // The whole claim rolled back: no row and no stamp for the second reminder either.
            (await verify.ReminderDeliveries.AsNoTracking().Where(r => r.UserId == owner).ToListAsync())
                .Should().ContainSingle().Which.SourceId.Should().Be(first);
            (await verify.Reminders.AsNoTracking().SingleAsync(r => r.Id == second)).LastDeliveredDate.Should().BeNull();
        }

        var nextSender = new RecordingReminderEmailSender();
        (await database.RunAsync(clock, nextSender)).Should().Be(1);

        // The first reminder belongs to the other claimant and is never sent by us.
        nextSender.DeliveriesFor(database.EmailOf(owner)).Should().Equal(("Second", Today));
    }

    [SqlServerFact]
    public async Task Parallel_workers_deliver_each_occurrence_exactly_once()
    {
        await using var database = await ReminderDatabase.CreateAsync();
        var clock = new FixedTimeProvider(Oct7Noon);
        var sender = new RecordingReminderEmailSender();
        var logger = new CapturingLogger<ReminderSchedulerRunner>();

        for (var iteration = 0; iteration < 3; iteration++)
        {
            var ownerA = await database.AddOwnerAsync(ReminderEmailDelivery.Immediate);
            var ownerB = await database.AddOwnerAsync(ReminderEmailDelivery.DailyDigest);
            var expected = new Dictionary<string, List<(string Title, DateOnly DueDate)>>();
            foreach (var (owner, reminderCount) in new[] { (ownerA, 3), (ownerB, 2) })
            {
                var person = await database.AddPersonAsync(owner, "Ada", p =>
                {
                    p.BirthdayMonth = 10;
                    p.BirthdayDay = 7;
                });
                var items = new List<(string, DateOnly)> { (ReminderEmailContent.BirthdayTitle, Today) };
                for (var i = 0; i < reminderCount; i++)
                {
                    var due = Today.AddDays(-i);
                    await database.AddReminderAsync(owner, person, $"Reminder {i}", due);
                    items.Add(($"Reminder {i}", due));
                }

                expected[owner] = items;
            }

            var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                await using var context = database.CreateDbContext(clock);
                await ReminderDatabase.CreateRunner(context, sender, clock, logger).RunDueRemindersJobAsync();
            }));
            await Task.WhenAll(workers);

            foreach (var (owner, items) in expected)
            {
                sender.DeliveriesFor(database.EmailOf(owner)).Should().BeEquivalentTo(
                    items, $"iteration {iteration}: every occurrence is delivered exactly once");
            }

            sender.DigestCountFor(database.EmailOf(ownerB)).Should().Be(1);
            await using var verify = database.CreateDbContext(clock);
            (await verify.ReminderDeliveries.AsNoTracking().CountAsync(r => r.UserId == ownerA)).Should().Be(4);
            (await verify.ReminderDeliveries.AsNoTracking().CountAsync(r => r.UserId == ownerB)).Should().Be(3);
        }

        logger.Errors.Should().BeEmpty("a lost claim is expected and quiet; an error would be a deadlock or a defect");
    }

    [SqlServerFact]
    public async Task Send_failure_then_retry_delivers_once()
    {
        await using var database = await ReminderDatabase.CreateAsync();
        var clock = new FixedTimeProvider(Oct7Noon);
        var owner = await database.AddOwnerAsync(ReminderEmailDelivery.Immediate, birthdays: false);
        var person = await database.AddPersonAsync(owner, "Ada");
        var reminder = await database.AddReminderAsync(owner, person, "Call", Today);
        var sender = new RecordingReminderEmailSender
        {
            FailWhen = (email, call) => email == database.EmailOf(owner) && call == 1
                ? new InvalidOperationException("smtp down")
                : null,
        };

        (await database.RunAsync(clock, sender)).Should().Be(0);

        await using (var verify = database.CreateDbContext(clock))
        {
            (await verify.ReminderDeliveries.AsNoTracking().AnyAsync(r => r.UserId == owner)).Should().BeFalse();
            (await verify.Reminders.AsNoTracking().SingleAsync(r => r.Id == reminder)).LastDeliveredDate.Should().BeNull();
        }

        (await database.RunAsync(clock, sender)).Should().Be(1);
        (await database.RunAsync(clock, sender)).Should().Be(0);

        sender.DeliveriesFor(database.EmailOf(owner)).Should().Equal(("Call", Today));
        await using var final = database.CreateDbContext(clock);
        (await final.ReminderDeliveries.AsNoTracking().SingleAsync(r => r.UserId == owner))
            .Status.Should().Be(ReminderDeliveryStatus.Sent);
    }

    [SqlServerFact]
    public async Task A_failed_item_does_not_block_the_rest_and_a_deleted_reminder_does_not_fail_the_final_save()
    {
        await using var database = await ReminderDatabase.CreateAsync();
        var clock = new FixedTimeProvider(Oct7Noon);
        var owner = await database.AddOwnerAsync(ReminderEmailDelivery.Immediate, birthdays: false);
        var person = await database.AddPersonAsync(owner, "Ada");
        var first = await database.AddReminderAsync(owner, person, "A", Today.AddDays(-2));
        var failing = await database.AddReminderAsync(owner, person, "B", Today.AddDays(-1));
        var last = await database.AddReminderAsync(owner, person, "C", Today);
        var sender = new RecordingReminderEmailSender
        {
            FailWhen = (_, call) =>
            {
                if (call != 2)
                {
                    return null;
                }

                // The failing item's reminder is deleted (another tab) before the outcomes are saved.
                using var other = database.CreateDbContext(clock);
                other.Reminders.Remove(other.Reminders.Single(r => r.Id == failing));
                other.SaveChanges();
                return new InvalidOperationException("smtp down");
            },
        };

        (await database.RunAsync(clock, sender)).Should().Be(2);

        sender.DeliveriesFor(database.EmailOf(owner)).Select(d => d.Title).Should().Equal("A", "C");
        await using var verify = database.CreateDbContext(clock);
        var ledger = await verify.ReminderDeliveries.AsNoTracking().Where(r => r.UserId == owner).ToListAsync();
        ledger.Select(r => r.SourceId).Should().BeEquivalentTo([first, last]);
        ledger.Should().AllSatisfy(r => r.Status.Should().Be(ReminderDeliveryStatus.Sent));
        (await database.RunAsync(clock, sender)).Should().Be(0);
    }

    [SqlServerFact]
    public async Task Finalize_failure_then_restart_never_resends()
    {
        await using var database = await ReminderDatabase.CreateAsync();
        var clock = new FixedTimeProvider(Oct7Noon);
        var owner = await database.AddOwnerAsync(ReminderEmailDelivery.Immediate, birthdays: false);
        var person = await database.AddPersonAsync(owner, "Ada");
        await database.AddReminderAsync(owner, person, "Call", Today);
        var sender = new RecordingReminderEmailSender();
        var fault = new FailReminderFinalizeInterceptor();

        (await database.RunAsync(clock, sender, fault)).Should().Be(1);

        fault.Fired.Should().BeTrue();
        await using (var verify = database.CreateDbContext(clock))
        {
            (await verify.ReminderDeliveries.AsNoTracking().SingleAsync(r => r.UserId == owner))
                .Status.Should().Be(ReminderDeliveryStatus.Pending);
        }

        // A restart: a new context, a new runner. The claimed email is not sent again.
        (await database.RunAsync(clock, sender)).Should().Be(0);
        sender.DeliveriesFor(database.EmailOf(owner)).Should().Equal(("Call", Today));
    }

    [SqlServerFact]
    public async Task Due_queries_translate_on_sql_server_for_time_zones_snooze_and_leap_day()
    {
        await using var database = await ReminderDatabase.CreateAsync();

        // 2026-10-07 10:30 UTC: Kiritimati (UTC+14) is on Oct 8, Pago Pago (UTC-11) is still on Oct 6.
        var firstClock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 30, 0, TimeSpan.Zero));
        var kiritimati = await database.AddOwnerAsync(ReminderEmailDelivery.Immediate, "Pacific/Kiritimati", birthdays: false);
        var pagoPago = await database.AddOwnerAsync(ReminderEmailDelivery.Immediate, "Pacific/Pago_Pago", birthdays: false);
        var leap = await database.AddOwnerAsync(ReminderEmailDelivery.Immediate);

        var kiriPerson = await database.AddPersonAsync(kiritimati, "Kiri");
        await database.AddReminderAsync(kiritimati, kiriPerson, "K due", new DateOnly(2026, 10, 8));
        await database.AddReminderAsync(kiritimati, kiriPerson, "K tomorrow", new DateOnly(2026, 10, 9));
        await database.AddReminderAsync(kiritimati, kiriPerson, "K snoozed", new DateOnly(2026, 10, 1),
            r => r.SnoozedUntilDate = new DateOnly(2026, 10, 9));

        var pagoPerson = await database.AddPersonAsync(pagoPago, "Pago");
        await database.AddReminderAsync(pagoPago, pagoPerson, "P due", new DateOnly(2026, 10, 6));
        await database.AddReminderAsync(pagoPago, pagoPerson, "P not yet", new DateOnly(2026, 10, 7));
        await database.AddReminderAsync(pagoPago, pagoPerson, "P snoozed", new DateOnly(2026, 10, 6),
            r => r.SnoozedUntilDate = new DateOnly(2026, 10, 8));

        await database.AddPersonAsync(leap, "Leap", p =>
        {
            p.BirthdayMonth = 2;
            p.BirthdayDay = 29;
        });

        var sender = new RecordingReminderEmailSender();
        await database.RunAsync(firstClock, sender);

        sender.DeliveriesFor(database.EmailOf(kiritimati)).Should().Equal(("K due", new DateOnly(2026, 10, 8)));
        sender.DeliveriesFor(database.EmailOf(pagoPago)).Should().Equal(("P due", new DateOnly(2026, 10, 6)));
        sender.DeliveriesFor(database.EmailOf(leap)).Should().BeEmpty();

        // 2027 is not a leap year: a 29 February birthday is observed on 28 February.
        var leapClock = new FixedTimeProvider(new DateTimeOffset(2027, 2, 28, 12, 0, 0, TimeSpan.Zero));
        await database.RunAsync(leapClock, sender);
        await database.RunAsync(leapClock, sender);

        sender.DeliveriesFor(database.EmailOf(leap)).Should().Equal((ReminderEmailContent.BirthdayTitle, new DateOnly(2027, 2, 28)));
    }

    [SqlServerFact]
    public async Task Purge_deletes_only_expired_rows_on_sql_server()
    {
        await using var database = await ReminderDatabase.CreateAsync();
        var clock = new FixedTimeProvider(Oct7Noon);
        var owner = await database.AddOwnerAsync(ReminderEmailDelivery.None);
        var expired = NewRow(owner, Guid.NewGuid(), new DateOnly(2026, 9, 1), claimed: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        var futureBirthday = NewRow(owner, Guid.NewGuid(), new DateOnly(2026, 10, 20), claimed: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        var recentClaim = NewRow(owner, Guid.NewGuid(), new DateOnly(2026, 10, 1), claimed: new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc));
        await using (var seed = database.CreateDbContext(clock))
        {
            seed.ReminderDeliveries.AddRange(expired, futureBirthday, recentClaim);
            await seed.SaveChangesAsync();
        }

        await database.RunAsync(clock, new RecordingReminderEmailSender());

        await using var verify = database.CreateDbContext(clock);
        (await verify.ReminderDeliveries.AsNoTracking().Select(r => r.Id).ToListAsync())
            .Should().BeEquivalentTo([futureBirthday.Id, recentClaim.Id]);
    }

    private static ReminderDelivery NewRow(
        string userId,
        Guid sourceId,
        DateOnly? occurrence = null,
        DateTime? claimed = null) => new()
    {
        UserId = userId,
        Kind = ReminderDeliveryKind.Reminder,
        SourceId = sourceId,
        OccurrenceDate = occurrence ?? Today,
        DeliveryDate = Today,
        Status = ReminderDeliveryStatus.Pending,
        ClaimedAtUtc = claimed ?? new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc),
    };

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly object _gate = new();
        private readonly List<string> _errors = [];

        public List<string> Errors
        {
            get
            {
                lock (_gate)
                {
                    return [.. _errors];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                lock (_gate)
                {
                    _errors.Add(formatter(state, exception));
                }
            }
        }
    }

    /// <summary>A throwaway migrated database for one test.</summary>
    private sealed class ReminderDatabase : IAsyncDisposable
    {
        private readonly string _connectionString;

        private ReminderDatabase(string connectionString) => _connectionString = connectionString;

        public static async Task<ReminderDatabase> CreateAsync()
        {
            var builder = new SqlConnectionStringBuilder(SqlServerTestEnvironment.ServerConnectionString)
            {
                InitialCatalog = $"Relio_ReminderDelivery_{Guid.NewGuid():N}",
            };
            var database = new ReminderDatabase(builder.ConnectionString);
            await using var dbContext = database.CreateDbContext(TimeProvider.System);
            await SqlServerTestDatabase.CreateAndMigrateAsync(dbContext);
            return database;
        }

        public async ValueTask DisposeAsync()
        {
            await using var dbContext = CreateDbContext(TimeProvider.System);
            await dbContext.Database.EnsureDeletedAsync();
        }

        public RelioDbContext CreateDbContext(TimeProvider clock, params IInterceptor[] interceptors)
        {
            var options = new DbContextOptionsBuilder<RelioDbContext>()
                .UseSqlServer(_connectionString)
                .AddInterceptors(interceptors)
                .Options;
            return new RelioDbContext(options, clock, FieldProtector);
        }

        public static ReminderSchedulerRunner CreateRunner(
            RelioDbContext dbContext,
            IReminderEmailSender sender,
            TimeProvider clock,
            ILogger<ReminderSchedulerRunner>? logger = null) =>
            new(dbContext, sender, clock, logger ?? NullLogger<ReminderSchedulerRunner>.Instance);

        public string EmailOf(string ownerId) => $"{ownerId}@example.com";

        public async Task<int> RunAsync(
            TimeProvider clock,
            IReminderEmailSender sender,
            params IInterceptor[] interceptors)
        {
            await using var dbContext = CreateDbContext(clock, interceptors);
            return await CreateRunner(dbContext, sender, clock).RunDueRemindersJobAsync();
        }

        public async Task<string> AddOwnerAsync(
            ReminderEmailDelivery delivery,
            string timeZoneId = "UTC",
            bool birthdays = true)
        {
            var id = $"rem-{Guid.NewGuid():N}";
            var email = EmailOf(id);
            await using var dbContext = CreateDbContext(TimeProvider.System);
            dbContext.Users.Add(new RelioUser
            {
                Id = id,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString(),
            });
            dbContext.UserProfiles.Add(new UserProfile
            {
                OwnerId = id,
                TimeZoneId = timeZoneId,
                ReminderEmailDelivery = delivery,
                BirthdayRemindersEnabled = birthdays,
                UnsubscribeToken = $"tok-{id}",
            });
            await dbContext.SaveChangesAsync();
            return id;
        }

        public async Task<Guid> AddPersonAsync(string ownerId, string firstName, Action<Person>? configure = null)
        {
            await using var dbContext = CreateDbContext(TimeProvider.System);
            var person = new Person { OwnerId = ownerId, FirstName = firstName };
            configure?.Invoke(person);
            dbContext.People.Add(person);
            await dbContext.SaveChangesAsync();
            return person.Id;
        }

        public async Task<Guid> AddReminderAsync(
            string ownerId,
            Guid personId,
            string title,
            DateOnly dueDate,
            Action<Reminder>? configure = null)
        {
            await using var dbContext = CreateDbContext(TimeProvider.System);
            var reminder = new Reminder { OwnerId = ownerId, PersonId = personId, Title = title, DueDate = dueDate };
            configure?.Invoke(reminder);
            dbContext.Reminders.Add(reminder);
            await dbContext.SaveChangesAsync();
            return reminder.Id;
        }
    }
}
