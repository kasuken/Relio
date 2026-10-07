using Microsoft.EntityFrameworkCore;
using Relio.Application.Interactions;
using Relio.Application.Ownership;
using Relio.Application.Security;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.Interactions;

/// <summary>
/// EF Core implementation of the current user's shared interactions. Each mutation owns its
/// participant links and recalculates affected people in the same save.
/// </summary>
public sealed class InteractionService(
    RelioDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IInteractionService
{
    /// <inheritdoc />
    public async Task<InteractionDetails?> GetAsync(
        Guid interactionId,
        CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var interaction = await dbContext.Interactions
            .AsNoTracking()
            .Where(item => item.Id == interactionId && item.OwnerId == ownerId)
            .Select(item => new
            {
                item.Id,
                item.OccurredOn,
                item.Kind,
                item.Description,
                item.CreatedAtUtc,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (interaction is null)
        {
            return null;
        }

        var participants = await (
            from participant in dbContext.InteractionParticipants.AsNoTracking()
            join person in dbContext.People.AsNoTracking()
                on participant.PersonId equals person.Id
            where participant.OwnerId == ownerId
                && participant.InteractionId == interactionId
                && person.OwnerId == ownerId
            orderby person.FirstName, person.LastName, person.Id
            select new
            {
                participant.PersonId,
                person.FirstName,
                person.LastName,
                person.IsArchived,
            })
            .ToListAsync(cancellationToken);

        return new InteractionDetails(
            interaction.Id,
            interaction.OccurredOn,
            interaction.Kind,
            interaction.Description,
            interaction.CreatedAtUtc,
            participants
                .Select(person => new InteractionParticipantDetails(
                    person.PersonId,
                    Person.FormatDisplayName(person.FirstName, person.LastName),
                    person.IsArchived))
                .ToArray());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InteractionParticipantOption>> ListParticipantCandidatesAsync(
        Guid profilePersonId,
        IReadOnlyCollection<Guid>? includedParticipantIds = null,
        CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();
        var includedIds = (includedParticipantIds ?? [])
            .Append(profilePersonId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        var people = await dbContext.People
            .AsNoTracking()
            .Where(person => person.OwnerId == ownerId
                && (!person.IsArchived || includedIds.Contains(person.Id)))
            .OrderBy(person => person.FirstName)
            .ThenBy(person => person.LastName)
            .ThenBy(person => person.Id)
            .Select(person => new
            {
                person.Id,
                person.FirstName,
                person.LastName,
                person.IsArchived,
            })
            .ToListAsync(cancellationToken);

        return people
            .Select(person => new InteractionParticipantOption(
                person.Id,
                Person.FormatDisplayName(person.FirstName, person.LastName),
                person.IsArchived))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(
        CreateInteractionRequest request,
        CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var today = await UserToday.GetAsync(dbContext, ownerId, timeProvider, cancellationToken);
            var participantIds = request.ParticipantIds;
            var errors = InteractionRules.Validate(
                request.OccurredOn,
                request.Kind,
                request.Description,
                participantIds,
                today,
                request.ProfilePersonId);
            if (errors.Count > 0)
            {
                throw new InteractionValidationException(errors);
            }

            var ids = participantIds!.ToArray();
            var people = await dbContext.People
                .Where(person => person.OwnerId == ownerId && ids.Contains(person.Id))
                .ToDictionaryAsync(person => person.Id, cancellationToken);

            if (people.Count != ids.Length)
            {
                throw new ForeignEntityNotOwnedException(ForeignEntityNames.People);
            }

            if (people.Values.Any(person => person.IsArchived && person.Id != request.ProfilePersonId))
            {
                throw new InteractionValidationException([InteractionValidationError.ArchivedParticipantNotAllowed]);
            }

            var interaction = new Interaction
            {
                OwnerId = ownerId,
                OccurredOn = request.OccurredOn,
                Kind = request.Kind,
                Description = InteractionRules.NormalizeDescription(request.Description),
            };
            dbContext.Interactions.Add(interaction);

            foreach (var personId in ids)
            {
                dbContext.InteractionParticipants.Add(new InteractionParticipant
                {
                    OwnerId = ownerId,
                    InteractionId = interaction.Id,
                    PersonId = personId,
                });
            }

            await PersonLastContactUpdater.RecalculateAsync(
                dbContext,
                ownerId,
                ids,
                pendingOccurredOn: request.OccurredOn,
                pendingParticipantIds: ids,
                cancellationToken: cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            return interaction.Id;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        Guid interactionId,
        UpdateInteractionRequest request,
        CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var interaction = await dbContext.Interactions
                .Include(item => item.Participants.Where(participant => participant.OwnerId == ownerId))
                .FirstOrDefaultAsync(
                    item => item.Id == interactionId && item.OwnerId == ownerId,
                    cancellationToken);
            if (interaction is null)
            {
                return false;
            }

            var today = await UserToday.GetAsync(dbContext, ownerId, timeProvider, cancellationToken);
            var participantIds = request.ParticipantIds;
            var errors = InteractionRules.Validate(
                request.OccurredOn,
                request.Kind,
                request.Description,
                participantIds,
                today);
            if (errors.Count > 0)
            {
                throw new InteractionValidationException(errors);
            }

            var ids = participantIds!.ToArray();
            var existingParticipants = interaction.Participants.ToArray();
            var existingIds = existingParticipants.Select(participant => participant.PersonId).ToHashSet();
            var people = await dbContext.People
                .Where(person => person.OwnerId == ownerId && ids.Contains(person.Id))
                .ToDictionaryAsync(person => person.Id, cancellationToken);

            if (people.Count != ids.Length)
            {
                throw new ForeignEntityNotOwnedException(ForeignEntityNames.People);
            }

            if (people.Values.Any(person => person.IsArchived && !existingIds.Contains(person.Id)))
            {
                throw new InteractionValidationException([InteractionValidationError.ArchivedParticipantNotAllowed]);
            }

            var selectedIds = ids.ToHashSet();
            dbContext.InteractionParticipants.RemoveRange(
                existingParticipants.Where(participant => !selectedIds.Contains(participant.PersonId)));

            foreach (var personId in ids.Where(personId => !existingIds.Contains(personId)))
            {
                dbContext.InteractionParticipants.Add(new InteractionParticipant
                {
                    OwnerId = ownerId,
                    InteractionId = interaction.Id,
                    PersonId = personId,
                });
            }

            interaction.OccurredOn = request.OccurredOn;
            interaction.Kind = request.Kind;
            interaction.Description = InteractionRules.NormalizeDescription(request.Description);

            var affectedPersonIds = existingIds.Union(selectedIds).ToArray();
            await PersonLastContactUpdater.RecalculateAsync(
                dbContext,
                ownerId,
                affectedPersonIds,
                excludedInteractionIds: [interaction.Id],
                pendingOccurredOn: request.OccurredOn,
                pendingParticipantIds: ids,
                cancellationToken: cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid interactionId, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        try
        {
            var interaction = await dbContext.Interactions
                .Include(item => item.Participants.Where(participant => participant.OwnerId == ownerId))
                .FirstOrDefaultAsync(
                    item => item.Id == interactionId && item.OwnerId == ownerId,
                    cancellationToken);
            if (interaction is null)
            {
                return false;
            }

            var participantIds = interaction.Participants
                .Select(participant => participant.PersonId)
                .Distinct()
                .ToArray();

            dbContext.InteractionParticipants.RemoveRange(interaction.Participants);
            dbContext.Interactions.Remove(interaction);

            await PersonLastContactUpdater.RecalculateAsync(
                dbContext,
                ownerId,
                participantIds,
                excludedInteractionIds: [interaction.Id],
                cancellationToken: cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }
}
