using Microsoft.EntityFrameworkCore;
using Relio.Application.Ownership;
using Relio.Application.Reminders;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Domain;

namespace Relio.Data.Reminders;

/// <summary>
/// EF Core implementation of <see cref="IReminderService"/>, scoped to the current user.
/// </summary>
public sealed class ReminderService(
    RelioDbContext dbContext,
    ICurrentUser currentUser,
    IUserTimeZoneService userTimeZoneService,
    TimeProvider timeProvider) : IReminderService
{
    /// <inheritdoc />
    public async Task<ReminderDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        return await dbContext.Reminders
            .AsNoTracking()
            .Include(r => r.Person)
            .Where(r => r.OwnerId == ownerId && r.Id == id)
            .Select(r => ToDto(r))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReminderDto>> ListAsync(bool includeCompleted = false, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var query = dbContext.Reminders
            .AsNoTracking()
            .Include(r => r.Person)
            .Where(r => r.OwnerId == ownerId);

        if (!includeCompleted)
        {
            query = query.Where(r => !r.IsCompleted);
        }

        var reminders = await query.ToListAsync(cancellationToken);

        // In-memory sort by EffectiveDueDate (since SnoozedUntilDate ?? DueDate is computed), then title
        return reminders
            .OrderBy(r => r.EffectiveDueDate)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReminderDto>> ListForPersonAsync(Guid personId, bool includeCompleted = false, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        // Foreign entity check: person must exist and belong to owner
        var personExists = await dbContext.People
            .AsNoTracking()
            .AnyAsync(p => p.OwnerId == ownerId && p.Id == personId, cancellationToken);

        if (!personExists)
        {
            throw new ForeignEntityNotOwnedException(ForeignEntityNames.People);
        }

        var query = dbContext.Reminders
            .AsNoTracking()
            .Include(r => r.Person)
            .Where(r => r.OwnerId == ownerId && r.PersonId == personId);

        if (!includeCompleted)
        {
            query = query.Where(r => !r.IsCompleted);
        }

        var reminders = await query.ToListAsync(cancellationToken);

        return reminders
            .OrderBy(r => r.EffectiveDueDate)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReminderDto>> ListDueAsync(DateOnly onOrBeforeDate, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        // We load non-completed reminders for active (non-archived) people
        var reminders = await dbContext.Reminders
            .AsNoTracking()
            .Include(r => r.Person)
            .Where(r => r.OwnerId == ownerId && !r.IsCompleted && !r.Person!.IsArchived)
            .ToListAsync(cancellationToken);

        return reminders
            .Where(r => r.EffectiveDueDate <= onOrBeforeDate)
            .OrderBy(r => r.EffectiveDueDate)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ReminderDto> CreateAsync(CreateReminderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = currentUser.RequireUserId();

        var errors = ReminderRules.Validate(request);
        if (errors.Count > 0)
        {
            throw new ReminderValidationException(errors);
        }

        // Validate foreign entity: Person must belong to user and not be archived
        var person = await dbContext.People
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.OwnerId == ownerId && p.Id == request.PersonId && !p.IsArchived, cancellationToken);

        if (person is null)
        {
            throw new ForeignEntityNotOwnedException(ForeignEntityNames.People);
        }

        var reminder = new Reminder
        {
            OwnerId = ownerId,
            PersonId = request.PersonId,
            Title = ReminderRules.NormalizeTitle(request.Title),
            DueDate = request.DueDate,
            Frequency = request.Frequency,
            CustomIntervalMonths = request.Frequency == ReminderFrequency.CustomMonths ? request.CustomIntervalMonths : null,
        };

        try
        {
            dbContext.Reminders.Add(reminder);
            await dbContext.SaveChangesAsync(cancellationToken);

            return new ReminderDto(
                reminder.Id,
                reminder.PersonId,
                person.DisplayName,
                reminder.Title,
                reminder.DueDate,
                reminder.Frequency,
                reminder.CustomIntervalMonths,
                reminder.SnoozedUntilDate,
                reminder.EffectiveDueDate,
                reminder.IsCompleted,
                reminder.CompletedAtUtc,
                reminder.LastDeliveredDate);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<ReminderDto?> UpdateAsync(Guid id, UpdateReminderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = currentUser.RequireUserId();

        var errors = ReminderRules.Validate(request);
        if (errors.Count > 0)
        {
            throw new ReminderValidationException(errors);
        }

        try
        {
            var reminder = await dbContext.Reminders
                .Include(r => r.Person)
                .SingleOrDefaultAsync(r => r.OwnerId == ownerId && r.Id == id, cancellationToken);

            if (reminder is null)
            {
                return null;
            }

            reminder.Title = ReminderRules.NormalizeTitle(request.Title);
            reminder.DueDate = request.DueDate;
            reminder.Frequency = request.Frequency;
            reminder.CustomIntervalMonths = request.Frequency == ReminderFrequency.CustomMonths ? request.CustomIntervalMonths : null;

            // If the user modified the due date, reset any active snooze if the due date is now on or after snooze
            if (reminder.SnoozedUntilDate is not null && reminder.DueDate > reminder.SnoozedUntilDate)
            {
                reminder.SnoozedUntilDate = null;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(reminder);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();
        var today = await userTimeZoneService.GetTodayAsync(cancellationToken);

        try
        {
            var reminder = await dbContext.Reminders
                .SingleOrDefaultAsync(r => r.OwnerId == ownerId && r.Id == id, cancellationToken);

            if (reminder is null)
            {
                return false;
            }

            if (reminder.Frequency == ReminderFrequency.Once)
            {
                reminder.IsCompleted = true;
                reminder.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            }
            else
            {
                // Advance to next occurrence
                reminder.DueDate = ReminderRecurrence.ComputeNextDueDate(
                    reminder.DueDate,
                    reminder.Frequency,
                    reminder.CustomIntervalMonths,
                    today);
                reminder.SnoozedUntilDate = null;
                reminder.LastDeliveredDate = null;
                reminder.IsCompleted = false;
                reminder.CompletedAtUtc = null;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> SnoozeAsync(Guid id, DateOnly snoozedUntilDate, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        try
        {
            var reminder = await dbContext.Reminders
                .SingleOrDefaultAsync(r => r.OwnerId == ownerId && r.Id == id, cancellationToken);

            if (reminder is null)
            {
                return false;
            }

            reminder.SnoozedUntilDate = snoozedUntilDate;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        try
        {
            var reminder = await dbContext.Reminders
                .SingleOrDefaultAsync(r => r.OwnerId == ownerId && r.Id == id, cancellationToken);

            if (reminder is null)
            {
                return false;
            }

            dbContext.Reminders.Remove(reminder);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BirthdayReminderDto>> ListDueBirthdaysAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var profile = await dbContext.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.OwnerId == ownerId, cancellationToken);

        var globallyEnabled = profile?.BirthdayRemindersEnabled ?? true;
        if (!globallyEnabled)
        {
            return [];
        }

        var defaultLeadDays = profile?.DefaultBirthdayLeadDays ?? 0;
        var today = await userTimeZoneService.GetTodayAsync(cancellationToken);

        var people = await dbContext.People
            .AsNoTracking()
            .Where(p => p.OwnerId == ownerId && !p.IsArchived && p.BirthdayDay != null && p.BirthdayMonth != null)
            .ToListAsync(cancellationToken);

        return people
            .Select(p => BirthdayReminderCalculator.Calculate(p, defaultLeadDays, today, globallyEnabled: true))
            .Where(b => b is not null && b.IsDue)
            .Select(b => b!)
            .OrderBy(b => b.BirthdayDate)
            .ThenBy(b => b.PersonDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BirthdayReminderDto>> ListUpcomingBirthdaysAsync(int daysAhead = 30, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var profile = await dbContext.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.OwnerId == ownerId, cancellationToken);

        var globallyEnabled = profile?.BirthdayRemindersEnabled ?? true;
        if (!globallyEnabled)
        {
            return [];
        }

        var defaultLeadDays = profile?.DefaultBirthdayLeadDays ?? 0;
        var today = await userTimeZoneService.GetTodayAsync(cancellationToken);

        var people = await dbContext.People
            .AsNoTracking()
            .Where(p => p.OwnerId == ownerId && !p.IsArchived && p.BirthdayDay != null && p.BirthdayMonth != null)
            .ToListAsync(cancellationToken);

        return people
            .Select(p => BirthdayReminderCalculator.Calculate(p, defaultLeadDays, today, globallyEnabled: true))
            .Where(b => b is not null && b.DaysUntilBirthday >= 0 && b.DaysUntilBirthday <= daysAhead)
            .Select(b => b!)
            .OrderBy(b => b.BirthdayDate)
            .ThenBy(b => b.PersonDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ReminderDto ToDto(Reminder reminder) => new(
        reminder.Id,
        reminder.PersonId,
        reminder.Person?.DisplayName ?? string.Empty,
        reminder.Title,
        reminder.DueDate,
        reminder.Frequency,
        reminder.CustomIntervalMonths,
        reminder.SnoozedUntilDate,
        reminder.EffectiveDueDate,
        reminder.IsCompleted,
        reminder.CompletedAtUtc,
        reminder.LastDeliveredDate);
}
