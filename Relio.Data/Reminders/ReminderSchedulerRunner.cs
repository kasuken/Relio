using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Relio.Application.Reminders;
using Relio.Application.Time;
using Relio.Domain;

namespace Relio.Data.Reminders;

/// <summary>
/// EF Core implementation of <see cref="IReminderSchedulerRunner"/> that delivers due reminders
/// per user time zone and guarantees idempotent delivery (epic #36, issue #39).
/// </summary>
public sealed class ReminderSchedulerRunner(
    RelioDbContext dbContext,
    IReminderEmailSender emailSender,
    TimeProvider timeProvider,
    ILogger<ReminderSchedulerRunner> logger) : IReminderSchedulerRunner
{
    /// <inheritdoc />
    public async Task<int> RunDueRemindersJobAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await dbContext.UserProfiles
            .AsNoTracking()
            .Where(p => p.ReminderEmailDelivery != ReminderEmailDelivery.None)
            .ToListAsync(cancellationToken);

        var totalDelivered = 0;

        foreach (var profile in profiles)
        {
            try
            {
                var user = await dbContext.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == profile.OwnerId, cancellationToken);

                if (user is null || !user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email) || user.IsDisabled)
                {
                    continue;
                }

                if (!TimeZoneIds.TryParse(profile.TimeZoneId, out var timeZone))
                {
                    timeZone = TimeZoneInfo.Utc;
                }

                var userToday = UserCalendar.Today(timeProvider, timeZone);

                var dueReminders = await dbContext.Reminders
                    .Include(r => r.Person)
                    .Where(r => r.OwnerId == profile.OwnerId
                        && !r.IsCompleted
                        && (r.LastDeliveredDate == null || r.LastDeliveredDate < userToday)
                        && ((r.SnoozedUntilDate != null && r.SnoozedUntilDate <= userToday)
                            || (r.SnoozedUntilDate == null && r.DueDate <= userToday))
                        && (r.Person == null || !r.Person.IsArchived))
                    .ToListAsync(cancellationToken);

                if (dueReminders.Count == 0)
                {
                    continue;
                }

                var sortedReminders = dueReminders
                    .OrderBy(r => r.EffectiveDueDate)
                    .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (profile.ReminderEmailDelivery == ReminderEmailDelivery.Immediate)
                {
                    foreach (var reminder in sortedReminders)
                    {
                        var personName = !string.IsNullOrWhiteSpace(reminder.Person?.DisplayName)
                            ? reminder.Person.DisplayName
                            : "Contact";

                        await emailSender.SendImmediateReminderAsync(
                            user.Email,
                            personName,
                            reminder.Title,
                            reminder.EffectiveDueDate,
                            profile.UnsubscribeToken,
                            cancellationToken);

                        reminder.LastDeliveredDate = userToday;
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                    totalDelivered += sortedReminders.Count;
                }
                else if (profile.ReminderEmailDelivery == ReminderEmailDelivery.DailyDigest)
                {
                    var items = sortedReminders.Select(r => new DigestReminderItem(
                        !string.IsNullOrWhiteSpace(r.Person?.DisplayName) ? r.Person.DisplayName : "Contact",
                        r.Title,
                        r.EffectiveDueDate)).ToList();

                    await emailSender.SendDailyDigestAsync(
                        user.Email,
                        items,
                        profile.UnsubscribeToken,
                        cancellationToken);

                    foreach (var reminder in sortedReminders)
                    {
                        reminder.LastDeliveredDate = userToday;
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                    totalDelivered += sortedReminders.Count;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to deliver reminders for user {OwnerId}", profile.OwnerId);
            }
            finally
            {
                dbContext.ChangeTracker.Clear();
            }
        }

        logger.LogInformation("Due reminders job completed. Delivered {DeliveredCount} reminders.", totalDelivered);
        return totalDelivered;
    }
}
