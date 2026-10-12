using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Relio.Application.Reminders;
using Relio.Application.Time;
using Relio.Data.Configurations;
using Relio.Domain;

namespace Relio.Data.Reminders;

/// <summary>
/// EF Core implementation of <see cref="IReminderSchedulerRunner"/>: delivers due reminders and
/// birthdays by email per user time zone and preference, at most once per occurrence (epic #36,
/// issues #39 and #121).
/// </summary>
/// <remarks>
/// <para>
/// <b>Occurrences.</b> A reminder's occurrence is its effective due date (the snooze date when
/// snoozed): snoozing to a new date, editing the date or completing a recurring reminder starts a
/// new occurrence and sends one new email; an overdue reminder is not re-sent every day. A
/// birthday's occurrence is the observed birthday date, sent once anywhere in its lead-days window
/// (so an app that was down on the first window day, or a user who turns email on mid-window, still
/// gets it). The delivery channel is not part of the key: switching between immediate and digest
/// after delivery never re-sends.
/// </para>
/// <para>
/// <b>Claim before send.</b> For each user one save inserts a <see cref="ReminderDelivery"/> row
/// per occurrence (and stamps <see cref="Reminder.LastDeliveredDate"/>); only then is anything sent.
/// A competing worker's claim loses on the unique index and sends nothing. A second save records
/// the outcome: sent rows become <see cref="ReminderDeliveryStatus.Sent"/>, and rows whose send
/// threw are released (deleted, stamp restored) so the next run retries them. Delivery is therefore
/// <b>at most once</b>: a crash or a failed second save after the claim leaves claimed rows that are
/// never re-sent. One lost email is accepted over a duplicate.
/// </para>
/// <para>
/// <b>Privacy.</b> The runner reads only ids, names, titles and dates. It never loads notes,
/// interactions, difficult moments or a person's narrative fields, and logs only ids, counts and
/// exception type names.
/// </para>
/// </remarks>
public sealed class ReminderSchedulerRunner(
    RelioDbContext dbContext,
    IReminderEmailSender emailSender,
    TimeProvider timeProvider,
    ILogger<ReminderSchedulerRunner> logger) : IReminderSchedulerRunner
{
    /// <summary>How long a delivery row is kept after it was claimed, in days.</summary>
    public const int DeliveryRetentionDays = 14;

    /// <summary>The most expired rows one run removes.</summary>
    public const int PurgeBatchSize = 500;

    /// <inheritdoc />
    public async Task<int> RunDueRemindersJobAsync(CancellationToken cancellationToken = default)
    {
        await PurgeExpiredDeliveriesAsync(cancellationToken);

        var profiles = await dbContext.UserProfiles
            .AsNoTracking()
            .Where(p => p.ReminderEmailDelivery != ReminderEmailDelivery.None)
            .ToListAsync(cancellationToken);

        var totalDelivered = 0;

        foreach (var profile in profiles)
        {
            try
            {
                totalDelivered += await DeliverForOwnerAsync(profile, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    "Reminder delivery failed for user {OwnerId} with exception type {ExceptionType}.",
                    profile.OwnerId,
                    ex.GetType().Name);
            }
            finally
            {
                dbContext.ChangeTracker.Clear();
            }
        }

        logger.LogInformation("Due reminders job completed. Delivered {DeliveredCount} notifications.", totalDelivered);
        return totalDelivered;
    }

    private async Task<int> DeliverForOwnerAsync(UserProfile profile, CancellationToken cancellationToken)
    {
        var ownerId = profile.OwnerId;

        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == ownerId, cancellationToken);

        if (user is null || !user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email) || user.IsDisabled)
        {
            return 0;
        }

        if (!TimeZoneIds.TryParse(profile.TimeZoneId, out var timeZone))
        {
            timeZone = TimeZoneInfo.Utc;
        }

        var userToday = UserCalendar.Today(timeProvider, timeZone);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var candidates = await FindReminderCandidatesAsync(ownerId, userToday, cancellationToken);
        if (profile.BirthdayRemindersEnabled)
        {
            candidates.AddRange(await FindBirthdayCandidatesAsync(profile, userToday, cancellationToken));
        }

        if (candidates.Count == 0)
        {
            return 0;
        }

        candidates = candidates
            .OrderBy(c => c.OccurrenceDate)
            .ThenBy(c => c.Kind)
            .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.SourceId)
            .ToList();

        if (!await TryClaimAsync(ownerId, candidates, userToday, nowUtc, cancellationToken))
        {
            return 0;
        }

        var outcome = profile.ReminderEmailDelivery == ReminderEmailDelivery.Immediate
            ? await SendImmediateAsync(user.Email, profile.UnsubscribeToken, candidates, cancellationToken)
            : await SendDigestAsync(user.Email, profile.UnsubscribeToken, candidates, cancellationToken);

        await FinalizeAsync(ownerId, outcome, userToday);

        if (outcome.FailureType is not null)
        {
            logger.LogWarning(
                "Reminder email delivery for user {OwnerId} failed with {ExceptionType}; {ReleasedCount} notifications will be retried.",
                ownerId,
                outcome.FailureType,
                outcome.Released.Count);
        }

        if (outcome.Cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return outcome.Sent.Count;
    }

    private async Task<List<DeliveryCandidate>> FindReminderCandidatesAsync(
        string ownerId,
        DateOnly userToday,
        CancellationToken cancellationToken)
    {
        // Tracked: the claim stamps LastDeliveredDate on these rows. A reminder stamped by the
        // previous scheduler (stamp on or after its effective date) is not a candidate again, and
        // the ledger lookup is the guard against a competing worker that already claimed it.
        var reminders = await dbContext.Reminders
            .Where(r => r.OwnerId == ownerId
                && !r.IsCompleted
                && (r.SnoozedUntilDate ?? r.DueDate) <= userToday
                && (r.LastDeliveredDate == null || r.LastDeliveredDate < (r.SnoozedUntilDate ?? r.DueDate))
                && (r.Person == null || !r.Person.IsArchived)
                && !dbContext.ReminderDeliveries.Any(d => d.UserId == ownerId
                    && d.Kind == ReminderDeliveryKind.Reminder
                    && d.SourceId == r.Id
                    && d.OccurrenceDate == (r.SnoozedUntilDate ?? r.DueDate)))
            .ToListAsync(cancellationToken);

        if (reminders.Count == 0)
        {
            return [];
        }

        // Names only: loading the person would decrypt narrative fields the email never uses.
        var personIds = reminders.Select(r => r.PersonId).Distinct().ToList();
        var people = await dbContext.People
            .AsNoTracking()
            .Where(p => p.OwnerId == ownerId && personIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FirstName, p.LastName })
            .ToListAsync(cancellationToken);
        var names = people.ToDictionary(p => p.Id, p => Person.FormatDisplayName(p.FirstName, p.LastName));

        return reminders
            .Select(r => new DeliveryCandidate(
                ReminderDeliveryKind.Reminder,
                r.Id,
                r.EffectiveDueDate,
                names.TryGetValue(r.PersonId, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : ReminderEmailContent.FallbackPersonName,
                r.Title)
            {
                TrackedReminder = r,
                PreviousStamp = r.LastDeliveredDate,
            })
            .ToList();
    }

    private async Task<List<DeliveryCandidate>> FindBirthdayCandidatesAsync(
        UserProfile profile,
        DateOnly userToday,
        CancellationToken cancellationToken)
    {
        var ownerId = profile.OwnerId;

        var birthdayPeople = await dbContext.People
            .AsNoTracking()
            .Where(person => person.OwnerId == ownerId
                && !person.IsArchived
                && person.BirthdayDay != null
                && person.BirthdayMonth != null)
            .Select(person => new
            {
                person.Id,
                person.FirstName,
                person.LastName,
                person.BirthdayYear,
                BirthdayMonth = person.BirthdayMonth!.Value,
                BirthdayDay = person.BirthdayDay!.Value,
                person.BirthdayReminderDisabled,
                person.BirthdayReminderLeadDays,
            })
            .ToListAsync(cancellationToken);

        var due = new List<(Guid PersonId, string Name, DateOnly BirthdayDate)>();
        foreach (var person in birthdayPeople)
        {
            if (!Birthday.TryCreate(person.BirthdayMonth, person.BirthdayDay, person.BirthdayYear, out var birthday))
            {
                continue;
            }

            var reminder = BirthdayReminderCalculator.Calculate(
                person.Id,
                Person.FormatDisplayName(person.FirstName, person.LastName),
                birthday,
                person.BirthdayReminderDisabled,
                person.BirthdayReminderLeadDays,
                profile.DefaultBirthdayLeadDays,
                userToday);

            if (reminder is { IsDue: true })
            {
                due.Add((person.Id, reminder.PersonDisplayName, reminder.BirthdayDate));
            }
        }

        if (due.Count == 0)
        {
            return [];
        }

        // An observed date before today can never be selected again, so only later rows matter.
        var claimed = await dbContext.ReminderDeliveries
            .AsNoTracking()
            .Where(d => d.UserId == ownerId
                && d.Kind == ReminderDeliveryKind.Birthday
                && d.OccurrenceDate >= userToday)
            .Select(d => new { d.SourceId, d.OccurrenceDate })
            .ToListAsync(cancellationToken);
        var claimedKeys = claimed.Select(d => (d.SourceId, d.OccurrenceDate)).ToHashSet();

        return due
            .Where(b => !claimedKeys.Contains((b.PersonId, b.BirthdayDate)))
            .Select(b => new DeliveryCandidate(
                ReminderDeliveryKind.Birthday,
                b.PersonId,
                b.BirthdayDate,
                string.IsNullOrWhiteSpace(b.Name) ? ReminderEmailContent.FallbackPersonName : b.Name,
                ReminderEmailContent.BirthdayTitle))
            .ToList();
    }

    /// <summary>
    /// Inserts one claim row per candidate and stamps the reminders, in one save. Returns false (and
    /// sends nothing) when another worker claimed first or a reminder changed under us.
    /// </summary>
    private async Task<bool> TryClaimAsync(
        string ownerId,
        List<DeliveryCandidate> candidates,
        DateOnly userToday,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates)
        {
            candidate.Row = new ReminderDelivery
            {
                UserId = ownerId,
                Kind = candidate.Kind,
                SourceId = candidate.SourceId,
                OccurrenceDate = candidate.OccurrenceDate,
                DeliveryDate = userToday,
                Status = ReminderDeliveryStatus.Pending,
                ClaimedAtUtc = nowUtc,
            };

            // Explicit Add, as for every new child: see AGENTS.md.
            dbContext.ReminderDeliveries.Add(candidate.Row);

            if (candidate.TrackedReminder is { } reminder)
            {
                reminder.LastDeliveredDate = userToday;
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation(
                "Reminder delivery for user {OwnerId} skipped: a reminder changed during the claim.",
                ownerId);
            return false;
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueIndexViolation(
            ex, ReminderDeliveryConfiguration.DeliveryKeyIndexName))
        {
            // The message quotes the key, so it is never logged.
            logger.LogInformation("Reminder delivery for user {OwnerId} was claimed by another worker.", ownerId);
            return false;
        }
    }

    private async Task<SendOutcome> SendImmediateAsync(
        string email,
        string? unsubscribeToken,
        List<DeliveryCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var outcome = new SendOutcome();

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];

            if (cancellationToken.IsCancellationRequested)
            {
                // Nothing in flight: the unattempted rest can be retried.
                outcome.Released.AddRange(candidates.Skip(i));
                outcome.Cancelled = true;
                break;
            }

            try
            {
                await emailSender.SendImmediateReminderAsync(
                    email,
                    candidate.PersonName,
                    candidate.Title,
                    candidate.OccurrenceDate,
                    unsubscribeToken,
                    cancellationToken);
                outcome.Sent.Add(candidate);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // This send was in flight and may have gone out, so it stays claimed.
                outcome.Released.AddRange(candidates.Skip(i + 1));
                outcome.Cancelled = true;
                break;
            }
            catch (Exception ex)
            {
                outcome.FailureType = ex.GetType().Name;
                outcome.Released.AddRange(candidates.Skip(i));
                break;
            }
        }

        return outcome;
    }

    private async Task<SendOutcome> SendDigestAsync(
        string email,
        string? unsubscribeToken,
        List<DeliveryCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var outcome = new SendOutcome();
        var items = candidates
            .Select(c => new DigestReminderItem(c.PersonName, c.Title, c.OccurrenceDate))
            .ToList();

        if (cancellationToken.IsCancellationRequested)
        {
            // Nothing is in flight yet: release the whole digest so shutdown does not lose it.
            outcome.Released.AddRange(candidates);
            outcome.Cancelled = true;
            return outcome;
        }

        try
        {
            await emailSender.SendDailyDigestAsync(email, items, unsubscribeToken, cancellationToken);
            outcome.Sent.AddRange(candidates);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The digest was in flight and may have gone out, so every item stays claimed.
            outcome.Cancelled = true;
        }
        catch (Exception ex)
        {
            outcome.FailureType = ex.GetType().Name;
            outcome.Released.AddRange(candidates);
        }

        return outcome;
    }

    /// <summary>
    /// Records the outcome: sent rows become Sent, failed rows are released. Never cancelled, so a
    /// shutdown does not strand claims, and never rethrown: emails already sent must still count,
    /// and a failure here leaves the rows claimed, which is the at-most-once side.
    /// </summary>
    private async Task FinalizeAsync(string ownerId, SendOutcome outcome, DateOnly userToday)
    {
        if (outcome.Sent.Count == 0 && outcome.Released.Count == 0)
        {
            return;
        }

        try
        {
            var sentAtUtc = timeProvider.GetUtcNow().UtcDateTime;

            foreach (var candidate in outcome.Sent)
            {
                candidate.Row!.Status = ReminderDeliveryStatus.Sent;
                candidate.Row.SentAtUtc = sentAtUtc;
            }

            foreach (var candidate in outcome.Released)
            {
                dbContext.ReminderDeliveries.Remove(candidate.Row!);

                if (candidate.TrackedReminder is { } reminder && reminder.LastDeliveredDate == userToday)
                {
                    reminder.LastDeliveredDate = candidate.PreviousStamp;
                }
            }

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Finalizing reminder delivery for user {OwnerId} failed with {ExceptionType}; claimed notifications will not be resent.",
                ownerId,
                ex.GetType().Name);
        }
    }

    /// <summary>
    /// Removes ledger rows past retention. Safe because an observed birthday date before the user's
    /// today is never selected again, and a reminder is guarded by the stamp set in the claim.
    /// </summary>
    private async Task PurgeExpiredDeliveriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = timeProvider.GetUtcNow();
            var claimedBefore = now.UtcDateTime.AddDays(-DeliveryRetentionDays);

            // Every time zone (UTC-12 to UTC+14) has passed this date.
            var occurredBefore = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-2);

            var expired = await dbContext.ReminderDeliveries
                .Where(d => d.ClaimedAtUtc < claimedBefore && d.OccurrenceDate < occurredBefore)
                .OrderBy(d => d.ClaimedAtUtc)
                .Take(PurgeBatchSize)
                .ToListAsync(cancellationToken);

            if (expired.Count == 0)
            {
                return;
            }

            dbContext.ReminderDeliveries.RemoveRange(expired);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Purging expired reminder deliveries failed with exception type {ExceptionType}.",
                ex.GetType().Name);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    private sealed record DeliveryCandidate(
        ReminderDeliveryKind Kind,
        Guid SourceId,
        DateOnly OccurrenceDate,
        string PersonName,
        string Title)
    {
        public Reminder? TrackedReminder { get; init; }

        public DateOnly? PreviousStamp { get; init; }

        public ReminderDelivery? Row { get; set; }
    }

    private sealed class SendOutcome
    {
        public List<DeliveryCandidate> Sent { get; } = [];

        public List<DeliveryCandidate> Released { get; } = [];

        public bool Cancelled { get; set; }

        public string? FailureType { get; set; }
    }
}
