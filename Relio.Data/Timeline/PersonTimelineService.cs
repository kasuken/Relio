using Microsoft.EntityFrameworkCore;
using Relio.Application.Interactions;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Application.Timeline;
using Relio.Domain;

namespace Relio.Data.Timeline;

/// <summary>
/// Reads interactions and notes as separate, bounded keyset streams, then merges only those rows
/// in memory. A timeline can grow without an ever-larger offset or a full-history load.
/// </summary>
public sealed class PersonTimelineService(RelioDbContext dbContext, ICurrentUser currentUser) : IPersonTimelineService
{
    private const int MaximumPageSize = 100;

    /// <inheritdoc />
    public async Task<PersonTimelinePage?> GetPageAsync(
        Guid personId,
        TimelineFilter filter = TimelineFilter.All,
        TimelineContinuation? continuation = null,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        if (!Enum.IsDefined(filter))
        {
            throw new ArgumentOutOfRangeException(nameof(filter));
        }

        ValidateContinuation(continuation);
        var boundedPageSize = Math.Clamp(pageSize, 1, MaximumPageSize);

        var personExists = await dbContext.People
            .AsNoTracking()
            .AnyAsync(person => person.Id == personId && person.OwnerId == ownerId, cancellationToken);
        if (!personExists)
        {
            return null;
        }

        if (filter == TimelineFilter.DifficultMoment)
        {
            // Difficult moments do not have a model or service yet; keep the filter as an explicit
            // extension seam rather than inventing placeholder timeline rows.
            return new PersonTimelinePage([], boundedPageSize, false, null);
        }

        var includeInteractions = filter is TimelineFilter.All or TimelineFilter.Interaction;
        var includeNotes = filter is TimelineFilter.All or TimelineFilter.Note;

        var interactions = includeInteractions
            ? await LoadInteractionsAsync(ownerId, personId, continuation?.Interaction, boundedPageSize + 1, cancellationToken)
            : [];
        var notes = includeNotes
            ? await LoadNotesAsync(ownerId, personId, continuation?.Note, boundedPageSize + 1, cancellationToken)
            : [];

        var timeZone = includeNotes
            ? await LoadTimeZoneAsync(ownerId, cancellationToken)
            : TimeZoneInfo.Utc;

        var candidates = interactions
            .Select((interaction, index) => new TimelineCandidate(new PersonTimelineEntry(
                interaction.Id,
                TimelineEntryKind.Interaction,
                interaction.OccurredOn,
                interaction.CreatedAtUtc,
                interaction.Description,
                interaction.Kind,
                false,
                []), 0, index))
            .Concat(notes.Select((note, index) => new TimelineCandidate(new PersonTimelineEntry(
                note.Id,
                TimelineEntryKind.Note,
                UserCalendar.ToUserDate(ToUtcInstant(note.CreatedAtUtc), timeZone),
                note.CreatedAtUtc,
                note.Text,
                null,
                note.IsPinned,
                []), 1, index)))
            .OrderByDescending(candidate => candidate.Entry.Date)
            .ThenByDescending(candidate => candidate.Entry.CreatedAtUtc)
            // Each stream arrives from SQL Server ordered by its indexed id. The stream rank keeps
            // that provider-defined Guid order intact while a fixed type tie-break makes mixed
            // entries deterministic when their date and timestamp are identical.
            .ThenBy(candidate => candidate.StreamOrder)
            .ThenBy(candidate => candidate.StreamIndex)
            .ToList();

        var hasMore = candidates.Count > boundedPageSize;
        var pageItems = candidates.Take(boundedPageSize).Select(candidate => candidate.Entry).ToArray();
        if (pageItems.Length == 0)
        {
            return new PersonTimelinePage([], boundedPageSize, false, null);
        }

        var interactionIds = pageItems
            .Where(entry => entry.Kind == TimelineEntryKind.Interaction)
            .Select(entry => entry.Id)
            .ToArray();
        var participantsByInteraction = interactionIds.Length == 0
            ? new Dictionary<Guid, IReadOnlyList<InteractionParticipantDetails>>()
            : await LoadParticipantsAsync(ownerId, interactionIds, cancellationToken);

        var completedItems = pageItems
            .Select(entry => entry.Kind == TimelineEntryKind.Interaction
                ? entry with { Participants = participantsByInteraction.GetValueOrDefault(entry.Id, []) }
                : entry)
            .ToArray();

        if (!hasMore)
        {
            return new PersonTimelinePage(completedItems, boundedPageSize, false, null);
        }

        var nextInteraction = continuation?.Interaction;
        var lastInteraction = completedItems.LastOrDefault(entry => entry.Kind == TimelineEntryKind.Interaction);
        if (lastInteraction is not null)
        {
            nextInteraction = new InteractionTimelineCursor(
                lastInteraction.Date,
                lastInteraction.CreatedAtUtc,
                lastInteraction.Id);
        }

        var nextNote = continuation?.Note;
        var lastNote = completedItems.LastOrDefault(entry => entry.Kind == TimelineEntryKind.Note);
        if (lastNote is not null)
        {
            nextNote = new NoteTimelineCursor(lastNote.CreatedAtUtc, lastNote.Id);
        }

        return new PersonTimelinePage(
            completedItems,
            boundedPageSize,
            true,
            new TimelineContinuation(nextInteraction, nextNote));
    }

    private async Task<List<InteractionRow>> LoadInteractionsAsync(
        string ownerId,
        Guid personId,
        InteractionTimelineCursor? cursor,
        int take,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Interactions
            .AsNoTracking()
            .Where(interaction => interaction.OwnerId == ownerId
                && interaction.Participants.Any(participant =>
                    participant.OwnerId == ownerId && participant.PersonId == personId));

        if (cursor is not null)
        {
            query = query.Where(interaction =>
                interaction.OccurredOn < cursor.OccurredOn
                || (interaction.OccurredOn == cursor.OccurredOn
                    && (interaction.CreatedAtUtc < cursor.CreatedAtUtc
                        || (interaction.CreatedAtUtc == cursor.CreatedAtUtc
                            && interaction.Id.CompareTo(cursor.Id) < 0))));
        }

        return await query
            .OrderByDescending(interaction => interaction.OccurredOn)
            .ThenByDescending(interaction => interaction.CreatedAtUtc)
            .ThenByDescending(interaction => interaction.Id)
            .Take(take)
            .Select(interaction => new InteractionRow(
                interaction.Id,
                interaction.OccurredOn,
                interaction.CreatedAtUtc,
                interaction.Kind,
                interaction.Description))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<NoteRow>> LoadNotesAsync(
        string ownerId,
        Guid personId,
        NoteTimelineCursor? cursor,
        int take,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Notes
            .AsNoTracking()
            .Where(note => note.OwnerId == ownerId && note.PersonId == personId);

        if (cursor is not null)
        {
            query = query.Where(note =>
                note.CreatedAtUtc < cursor.CreatedAtUtc
                || (note.CreatedAtUtc == cursor.CreatedAtUtc
                    && note.Id.CompareTo(cursor.Id) < 0));
        }

        return await query
            .OrderByDescending(note => note.CreatedAtUtc)
            .ThenByDescending(note => note.Id)
            .Take(take)
            .Select(note => new NoteRow(note.Id, note.CreatedAtUtc, note.Text, note.IsPinned))
            .ToListAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, IReadOnlyList<InteractionParticipantDetails>>> LoadParticipantsAsync(
        string ownerId,
        Guid[] interactionIds,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from participant in dbContext.InteractionParticipants.AsNoTracking()
            join person in dbContext.People.AsNoTracking()
                on participant.PersonId equals person.Id
            where participant.OwnerId == ownerId
                && interactionIds.Contains(participant.InteractionId)
                && person.OwnerId == ownerId
            orderby person.FirstName, person.LastName, person.Id
            select new ParticipantRow(
                participant.InteractionId,
                participant.PersonId,
                person.FirstName,
                person.LastName,
                person.IsArchived))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.InteractionId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<InteractionParticipantDetails>)group
                    .Select(row => new InteractionParticipantDetails(
                        row.PersonId,
                        Person.FormatDisplayName(row.FirstName, row.LastName),
                        row.IsArchived))
                    .ToArray());
    }

    private async Task<TimeZoneInfo> LoadTimeZoneAsync(string ownerId, CancellationToken cancellationToken)
    {
        var timeZoneId = await dbContext.UserProfiles
            .AsNoTracking()
            .Where(profile => profile.OwnerId == ownerId)
            .Select(profile => profile.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);

        return TimeZoneIds.TryParse(timeZoneId, out var timeZone)
            ? timeZone
            : TimeZoneInfo.Utc;
    }

    private static void ValidateContinuation(TimelineContinuation? continuation)
    {
        if (continuation?.Interaction is { } interaction
            && (interaction.Id == Guid.Empty || interaction.OccurredOn == default || interaction.CreatedAtUtc == default))
        {
            throw new ArgumentException("The interaction timeline cursor is invalid.", nameof(continuation));
        }

        if (continuation?.Note is { } note
            && (note.Id == Guid.Empty || note.CreatedAtUtc == default))
        {
            throw new ArgumentException("The note timeline cursor is invalid.", nameof(continuation));
        }
    }

    private static DateTimeOffset ToUtcInstant(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record InteractionRow(
        Guid Id,
        DateOnly OccurredOn,
        DateTime CreatedAtUtc,
        InteractionKind Kind,
        string Description);

    private sealed record NoteRow(Guid Id, DateTime CreatedAtUtc, string Text, bool IsPinned);

    private sealed record ParticipantRow(
        Guid InteractionId,
        Guid PersonId,
        string FirstName,
        string? LastName,
        bool IsArchived);

    private sealed record TimelineCandidate(PersonTimelineEntry Entry, int StreamOrder, int StreamIndex);
}
