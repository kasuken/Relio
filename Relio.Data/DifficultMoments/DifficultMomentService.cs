using Microsoft.EntityFrameworkCore;
using Relio.Application.DifficultMoments;
using Relio.Application.Ownership;
using Relio.Application.Security;
using Relio.Data.People;
using Relio.Domain;

namespace Relio.Data.DifficultMoments;

/// <summary>
/// Data service for managing user-scoped difficult moments and reflections.
/// </summary>
public sealed class DifficultMomentService(
    RelioDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IDifficultMomentService
{
    /// <inheritdoc />
    public async Task<DifficultMomentDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var moment = await dbContext.DifficultMoments
            .AsNoTracking()
            .Include(m => m.Person)
            .Include(m => m.RecurrenceOf)
            .Include(m => m.Recurrences)
            .Where(m => m.Id == id && m.OwnerId == ownerId)
            .SingleOrDefaultAsync(cancellationToken);

        if (moment is null)
        {
            return null;
        }

        return ToDetails(moment);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DifficultMomentSummary>> ListForPersonAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var moments = await dbContext.DifficultMoments
            .AsNoTracking()
            .Include(m => m.Recurrences)
            .Where(m => m.OwnerId == ownerId && m.PersonId == personId)
            .OrderByDescending(m => m.OccurredOn)
            .ThenByDescending(m => m.CreatedAtUtc)
            .ThenByDescending(m => m.Id)
            .ToListAsync(cancellationToken);

        return moments.Select(m => new DifficultMomentSummary(
            m.Id,
            m.PersonId,
            m.OccurredOn,
            m.Description,
            m.Status,
            m.ResolvedOn,
            m.RecurrenceOfId,
            m.Recurrences.Count,
            m.CreatedAtUtc)).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DifficultMomentOverviewItem>> ListOverviewAsync(DifficultMomentFilterRequest filter, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var query = dbContext.DifficultMoments
            .AsNoTracking()
            .Include(m => m.Person)
            .Include(m => m.Recurrences)
            .Where(m => m.OwnerId == ownerId);

        if (!filter.IncludeArchived)
        {
            query = query.Where(m => !m.Person.IsArchived);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(m => m.Status == filter.Status.Value);
        }

        if (filter.PersonId.HasValue)
        {
            query = query.Where(m => m.PersonId == filter.PersonId.Value);
        }

        var moments = await query
            .OrderByDescending(m => m.OccurredOn)
            .ThenByDescending(m => m.CreatedAtUtc)
            .ThenByDescending(m => m.Id)
            .ToListAsync(cancellationToken);

        return moments.Select(m => new DifficultMomentOverviewItem(
            m.Id,
            m.PersonId,
            m.Person.DisplayName,
            m.Person.IsArchived,
            m.OccurredOn,
            m.Description,
            m.Trigger,
            m.Resolution,
            m.LessonsLearned,
            m.Status,
            m.ResolvedOn,
            m.RecurrenceOfId,
            m.Recurrences.Count,
            m.CreatedAtUtc)).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DifficultMomentLookupItem>> ListCandidatesForRecurrenceAsync(
        Guid personId,
        Guid? excludeMomentId = null,
        CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var query = dbContext.DifficultMoments
            .AsNoTracking()
            .Where(m => m.OwnerId == ownerId && m.PersonId == personId);

        if (excludeMomentId.HasValue)
        {
            query = query.Where(m => m.Id != excludeMomentId.Value);
        }

        var moments = await query
            .OrderByDescending(m => m.OccurredOn)
            .ThenByDescending(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return moments.Select(m => new DifficultMomentLookupItem(
            m.Id,
            m.OccurredOn,
            m.Description,
            m.Status)).ToArray();
    }

    /// <inheritdoc />
    public async Task<DifficultMomentDetails> CreateAsync(CreateDifficultMomentRequest request, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();
        var today = await UserToday.GetAsync(dbContext, ownerId, timeProvider, cancellationToken);

        var errors = DifficultMomentRules.Validate(
            request.OccurredOn,
            request.Description,
            request.Trigger,
            request.Resolution,
            request.LessonsLearned,
            request.Status,
            request.ResolvedOn,
            request.RecurrenceOfId,
            null,
            today);

        if (errors.Count > 0)
        {
            throw new DifficultMomentValidationException(errors);
        }

        var person = await dbContext.People
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PersonId && p.OwnerId == ownerId, cancellationToken);

        if (person is null)
        {
            throw new ForeignEntityNotOwnedException(ForeignEntityNames.People);
        }

        if (request.RecurrenceOfId.HasValue)
        {
            var recurrenceOf = await dbContext.DifficultMoments
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == request.RecurrenceOfId.Value && m.OwnerId == ownerId && m.PersonId == request.PersonId, cancellationToken);

            if (recurrenceOf is null)
            {
                throw new ForeignEntityNotOwnedException(ForeignEntityNames.DifficultMoments);
            }
        }

        try
        {
            var moment = new DifficultMoment
            {
                OwnerId = ownerId,
                PersonId = request.PersonId,
                OccurredOn = request.OccurredOn,
                Description = DifficultMomentRules.NormalizeDescription(request.Description),
                Trigger = DifficultMomentRules.NormalizeOptionalText(request.Trigger),
                Resolution = DifficultMomentRules.NormalizeOptionalText(request.Resolution),
                LessonsLearned = DifficultMomentRules.NormalizeOptionalText(request.LessonsLearned),
                Status = request.Status,
                ResolvedOn = request.Status == DifficultMomentStatus.Resolved ? (request.ResolvedOn ?? today) : null,
                RecurrenceOfId = request.RecurrenceOfId,
            };

            dbContext.DifficultMoments.Add(moment);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await GetAsync(moment.Id, cancellationToken)
                ?? throw new InvalidOperationException("Failed to load created difficult moment.");
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(Guid id, UpdateDifficultMomentRequest request, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();
        var today = await UserToday.GetAsync(dbContext, ownerId, timeProvider, cancellationToken);

        var errors = DifficultMomentRules.Validate(
            request.OccurredOn,
            request.Description,
            request.Trigger,
            request.Resolution,
            request.LessonsLearned,
            request.Status,
            request.ResolvedOn,
            request.RecurrenceOfId,
            id,
            today);

        if (errors.Count > 0)
        {
            throw new DifficultMomentValidationException(errors);
        }

        try
        {
            var moment = await dbContext.DifficultMoments
                .FirstOrDefaultAsync(m => m.Id == id && m.OwnerId == ownerId, cancellationToken);

            if (moment is null)
            {
                return false;
            }

            if (request.RecurrenceOfId.HasValue)
            {
                var recurrenceOf = await dbContext.DifficultMoments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == request.RecurrenceOfId.Value && m.OwnerId == ownerId && m.PersonId == moment.PersonId, cancellationToken);

                if (recurrenceOf is null)
                {
                    throw new ForeignEntityNotOwnedException(ForeignEntityNames.DifficultMoments);
                }
            }

            moment.OccurredOn = request.OccurredOn;
            moment.Description = DifficultMomentRules.NormalizeDescription(request.Description);
            moment.Trigger = DifficultMomentRules.NormalizeOptionalText(request.Trigger);
            moment.Resolution = DifficultMomentRules.NormalizeOptionalText(request.Resolution);
            moment.LessonsLearned = DifficultMomentRules.NormalizeOptionalText(request.LessonsLearned);
            moment.Status = request.Status;
            moment.ResolvedOn = request.Status == DifficultMomentStatus.Resolved ? (request.ResolvedOn ?? today) : null;
            moment.RecurrenceOfId = request.RecurrenceOfId;

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
            var moment = await dbContext.DifficultMoments
                .Include(m => m.Recurrences)
                .FirstOrDefaultAsync(m => m.Id == id && m.OwnerId == ownerId, cancellationToken);

            if (moment is null)
            {
                return false;
            }

            foreach (var recurrence in moment.Recurrences)
            {
                recurrence.RecurrenceOfId = null;
            }

            dbContext.DifficultMoments.Remove(moment);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    private static DifficultMomentDetails ToDetails(DifficultMoment moment) =>
        new(
            moment.Id,
            moment.PersonId,
            moment.Person?.DisplayName ?? string.Empty,
            moment.Person?.IsArchived ?? false,
            moment.OccurredOn,
            moment.Description,
            moment.Trigger,
            moment.Resolution,
            moment.LessonsLearned,
            moment.Status,
            moment.ResolvedOn,
            moment.RecurrenceOfId,
            moment.RecurrenceOf?.Description,
            moment.Recurrences
                .OrderByDescending(r => r.OccurredOn)
                .ThenByDescending(r => r.CreatedAtUtc)
                .Select(r => new DifficultMomentRecurrenceItem(r.Id, r.OccurredOn, r.Description, r.Status))
                .ToArray(),
            moment.CreatedAtUtc,
            moment.UpdatedAtUtc);
}
