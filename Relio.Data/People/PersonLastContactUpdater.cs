using Microsoft.EntityFrameworkCore;

namespace Relio.Data.People;

/// <summary>
/// Recomputes the denormalized last-contacted calendar date from the interactions that remain
/// attached to each person. Caller mutations are still pending, so excluded and replacement
/// interaction values are supplied explicitly and saved with the same unit of work.
/// </summary>
internal static class PersonLastContactUpdater
{
    /// <summary>
    /// Recalculates current owners' <c>Person.LastContactedOn</c> values in the existing tracked
    /// unit of work. <paramref name="excludedInteractionIds"/> are interactions being edited or
    /// removed; <paramref name="pendingOccurredOn"/> and <paramref name="pendingParticipantIds"/>
    /// describe a not-yet-saved create or replacement.
    /// </summary>
    public static async Task RecalculateAsync(
        RelioDbContext dbContext,
        string ownerId,
        IReadOnlyCollection<Guid> personIds,
        IReadOnlyCollection<Guid>? excludedInteractionIds = null,
        DateOnly? pendingOccurredOn = null,
        IReadOnlyCollection<Guid>? pendingParticipantIds = null,
        IReadOnlyDictionary<Guid, IReadOnlyCollection<Guid>>? combinedPersonIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(personIds);

        var ids = personIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var sourceIds = ids
            .Concat(combinedPersonIds?.Values.SelectMany(sourcePersonIds => sourcePersonIds) ?? Enumerable.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        var interactionDates =
            from participant in dbContext.InteractionParticipants.AsNoTracking()
            join interaction in dbContext.Interactions.AsNoTracking()
                on participant.InteractionId equals interaction.Id
            where participant.OwnerId == ownerId
                && interaction.OwnerId == ownerId
                && sourceIds.Contains(participant.PersonId)
            select new { participant.PersonId, participant.InteractionId, interaction.OccurredOn };

        if (excludedInteractionIds is { Count: > 0 })
        {
            var excludedIds = excludedInteractionIds.Distinct().ToArray();
            interactionDates = interactionDates.Where(row => !excludedIds.Contains(row.InteractionId));
        }

        var latestByPerson = await interactionDates
            .GroupBy(row => row.PersonId)
            .Select(group => new { PersonId = group.Key, OccurredOn = group.Max(row => row.OccurredOn) })
            .ToDictionaryAsync(row => row.PersonId, row => row.OccurredOn, cancellationToken);

        if (pendingOccurredOn is { } pendingDate && pendingParticipantIds is { Count: > 0 })
        {
            foreach (var personId in pendingParticipantIds)
            {
                if (!latestByPerson.TryGetValue(personId, out var latest) || pendingDate > latest)
                {
                    latestByPerson[personId] = pendingDate;
                }
            }
        }

        var people = await dbContext.People
            .Where(person => person.OwnerId == ownerId && ids.Contains(person.Id))
            .ToListAsync(cancellationToken);

        foreach (var person in people)
        {
            IEnumerable<Guid> sourcePersonIds = combinedPersonIds is not null
                && combinedPersonIds.TryGetValue(person.Id, out var combinedIds)
                    ? combinedIds
                    : new[] { person.Id };
            DateOnly? latest = null;
            foreach (var sourcePersonId in sourcePersonIds)
            {
                if (latestByPerson.TryGetValue(sourcePersonId, out var sourceLatest)
                    && (latest is null || sourceLatest > latest))
                {
                    latest = sourceLatest;
                }
            }

            person.LastContactedOn = latest;
        }
    }
}
