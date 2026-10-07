using Microsoft.EntityFrameworkCore;
using Relio.Application.Dashboard;
using Relio.Application.Reminders;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Domain;

namespace Relio.Data.Dashboard;

/// <summary>
/// EF Core implementation of the current user's dashboard overview. Every read is explicitly
/// owner-scoped and projects only the fields shown by the dashboard.
/// </summary>
public sealed class DashboardService(
    RelioDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IDashboardService
{
    private const int SectionLimit = 5;
    private const int UpcomingDays = 30;

    private readonly RelioDbContext _dbContext = dbContext
        ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly ICurrentUser _currentUser = currentUser
        ?? throw new ArgumentNullException(nameof(currentUser));
    private readonly TimeProvider _timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public async Task<DashboardSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = _currentUser.RequireUserId();

        var profile = await _dbContext.UserProfiles
            .AsNoTracking()
            .Where(userProfile => userProfile.OwnerId == ownerId)
            .Select(userProfile => new
            {
                userProfile.TimeZoneId,
                userProfile.BirthdayRemindersEnabled,
                userProfile.DefaultBirthdayLeadDays,
            })
            .SingleOrDefaultAsync(cancellationToken);

        var timeZone = TimeZoneIds.TryParse(profile?.TimeZoneId, out var parsedTimeZone)
            ? parsedTimeZone
            : TimeZoneInfo.Utc;
        var today = UserCalendar.Today(_timeProvider, timeZone);
        var lastUpcomingDate = today.AddDays(UpcomingDays);

        var peopleCounts = await _dbContext.People
            .AsNoTracking()
            .Where(person => person.OwnerId == ownerId)
            .GroupBy(person => person.IsArchived)
            .Select(group => new
            {
                IsArchived = group.Key,
                Count = group.Count(),
            })
            .ToListAsync(cancellationToken);
        var activePeopleCount = peopleCounts
            .Where(count => !count.IsArchived)
            .Select(count => count.Count)
            .SingleOrDefault();
        var archivedPeopleCount = peopleCounts
            .Where(count => count.IsArchived)
            .Select(count => count.Count)
            .SingleOrDefault();

        var reminderRows = await (
            from reminder in _dbContext.Reminders.AsNoTracking()
            join person in _dbContext.People.AsNoTracking()
                on reminder.PersonId equals person.Id
            where reminder.OwnerId == ownerId
                && person.OwnerId == ownerId
                && !person.IsArchived
                && !reminder.IsCompleted
                && (reminder.SnoozedUntilDate ?? reminder.DueDate) <= lastUpcomingDate
            orderby reminder.SnoozedUntilDate ?? reminder.DueDate,
                reminder.CreatedAtUtc descending,
                reminder.Id
            select new
            {
                reminder.Id,
                reminder.PersonId,
                person.FirstName,
                person.LastName,
                reminder.Title,
                reminder.DueDate,
                reminder.Frequency,
                reminder.CustomIntervalMonths,
                reminder.SnoozedUntilDate,
                EffectiveDueDate = reminder.SnoozedUntilDate ?? reminder.DueDate,
            })
            .Take(SectionLimit)
            .ToListAsync(cancellationToken);
        var upcomingReminders = reminderRows
            .Select(reminder => new DashboardReminderItem(
                reminder.Id,
                reminder.PersonId,
                Person.FormatDisplayName(reminder.FirstName, reminder.LastName),
                reminder.Title,
                reminder.DueDate,
                reminder.Frequency,
                reminder.CustomIntervalMonths,
                reminder.SnoozedUntilDate,
                reminder.EffectiveDueDate))
            .ToArray();

        var upcomingBirthdays = await GetUpcomingBirthdaysAsync(
            ownerId,
            profile?.BirthdayRemindersEnabled ?? true,
            profile?.DefaultBirthdayLeadDays ?? 0,
            today,
            cancellationToken);

        var reachOuts = await GetReachOutsAsync(ownerId, today, timeZone, cancellationToken);
        var recentInteractions = await GetRecentInteractionsAsync(ownerId, cancellationToken);
        var recentlyAddedPeople = await GetRecentlyAddedPeopleAsync(ownerId, timeZone, cancellationToken);

        return new DashboardSnapshot(
            today,
            activePeopleCount,
            archivedPeopleCount,
            upcomingReminders,
            upcomingBirthdays,
            reachOuts,
            recentInteractions,
            recentlyAddedPeople);
    }

    private async Task<IReadOnlyList<BirthdayReminderDto>> GetUpcomingBirthdaysAsync(
        string ownerId,
        bool globallyEnabled,
        int defaultLeadDays,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (!globallyEnabled)
        {
            return [];
        }

        var birthdayPeople = await _dbContext.People
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

        return birthdayPeople
            .Select(person =>
            {
                if (!Birthday.TryCreate(person.BirthdayMonth, person.BirthdayDay, person.BirthdayYear, out var birthday))
                {
                    return null;
                }

                return BirthdayReminderCalculator.Calculate(
                    person.Id,
                    Person.FormatDisplayName(person.FirstName, person.LastName),
                    birthday,
                    person.BirthdayReminderDisabled,
                    person.BirthdayReminderLeadDays,
                    defaultLeadDays,
                    today);
            })
            .Where(birthday => birthday is not null
                && birthday.DaysUntilBirthday >= 0
                && birthday.DaysUntilBirthday <= UpcomingDays)
            .Select(birthday => birthday!)
            .OrderBy(birthday => birthday.BirthdayDate)
            .ThenBy(birthday => birthday.PersonId)
            .Take(SectionLimit)
            .ToArray();
    }

    private async Task<IReadOnlyList<ReachOutDto>> GetReachOutsAsync(
        string ownerId,
        DateOnly today,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var cadencePeople = await _dbContext.People
            .AsNoTracking()
            .Where(person => person.OwnerId == ownerId
                && !person.IsArchived
                && person.StayInTouchCadenceDays != null)
            .Select(person => new
            {
                person.Id,
                person.FirstName,
                person.LastName,
                person.LastContactedOn,
                person.CreatedAtUtc,
                person.StayInTouchCadenceDays,
            })
            .ToListAsync(cancellationToken);

        return cadencePeople
            .Select(person => ReachOutCalculator.Calculate(
                person.Id,
                Person.FormatDisplayName(person.FirstName, person.LastName),
                person.LastContactedOn,
                person.CreatedAtUtc,
                person.StayInTouchCadenceDays,
                today,
                timeZone))
            .Where(reachOut => reachOut is not null)
            .Select(reachOut => reachOut!)
            .OrderByDescending(reachOut => reachOut.DaysOverdue)
            .ThenBy(reachOut => reachOut.ReferenceDate)
            .ThenBy(reachOut => reachOut.PersonId)
            .Take(SectionLimit)
            .ToArray();
    }

    private async Task<IReadOnlyList<DashboardInteractionItem>> GetRecentInteractionsAsync(
        string ownerId,
        CancellationToken cancellationToken)
    {
        var interactionRows = await _dbContext.Interactions
            .AsNoTracking()
            .Where(interaction => interaction.OwnerId == ownerId
                && _dbContext.InteractionParticipants
                    .AsNoTracking()
                    .Any(participant => participant.OwnerId == ownerId
                        && participant.InteractionId == interaction.Id
                        && _dbContext.People
                            .AsNoTracking()
                            .Any(person => person.Id == participant.PersonId
                                && person.OwnerId == ownerId
                                && !person.IsArchived)))
            .OrderByDescending(interaction => interaction.OccurredOn)
            .ThenByDescending(interaction => interaction.CreatedAtUtc)
            .ThenBy(interaction => interaction.Id)
            .Take(SectionLimit)
            .Select(interaction => new
            {
                interaction.Id,
                interaction.OccurredOn,
                interaction.Kind,
                interaction.Description,
            })
            .ToListAsync(cancellationToken);

        if (interactionRows.Count == 0)
        {
            return [];
        }

        var interactionIds = interactionRows.Select(interaction => interaction.Id).ToArray();
        var participantRows = await (
            from participant in _dbContext.InteractionParticipants.AsNoTracking()
            join interaction in _dbContext.Interactions.AsNoTracking()
                on participant.InteractionId equals interaction.Id
            join person in _dbContext.People.AsNoTracking()
                on participant.PersonId equals person.Id
            where participant.OwnerId == ownerId
                && interaction.OwnerId == ownerId
                && interactionIds.Contains(interaction.Id)
                && person.OwnerId == ownerId
                && !person.IsArchived
            orderby interaction.Id, person.FirstName, person.LastName, person.Id
            select new
            {
                InteractionId = interaction.Id,
                PersonId = person.Id,
                person.FirstName,
                person.LastName,
            })
            .ToListAsync(cancellationToken);

        var participantsByInteraction = participantRows
            .GroupBy(participant => participant.InteractionId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<DashboardInteractionParticipant>)group
                    .Select(participant => new DashboardInteractionParticipant(
                        participant.PersonId,
                        Person.FormatDisplayName(participant.FirstName, participant.LastName)))
                    .ToArray());

        return interactionRows
            .Where(interaction => participantsByInteraction.ContainsKey(interaction.Id))
            .Select(interaction => new DashboardInteractionItem(
                interaction.Id,
                interaction.OccurredOn,
                interaction.Kind,
                interaction.Description,
                participantsByInteraction.TryGetValue(interaction.Id, out var participants)
                    ? participants
                    : Array.Empty<DashboardInteractionParticipant>()))
            .ToArray();
    }

    private async Task<IReadOnlyList<DashboardPersonItem>> GetRecentlyAddedPeopleAsync(
        string ownerId,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var people = await _dbContext.People
            .AsNoTracking()
            .Where(person => person.OwnerId == ownerId && !person.IsArchived)
            .OrderByDescending(person => person.CreatedAtUtc)
            .ThenBy(person => person.Id)
            .Take(SectionLimit)
            .Select(person => new
            {
                person.Id,
                person.FirstName,
                person.LastName,
                person.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        return people
            .Select(person => new DashboardPersonItem(
                person.Id,
                Person.FormatDisplayName(person.FirstName, person.LastName),
                UserCalendar.ToUserDate(
                    new DateTimeOffset(DateTime.SpecifyKind(person.CreatedAtUtc, DateTimeKind.Utc)),
                    timeZone)))
            .ToArray();
    }
}
