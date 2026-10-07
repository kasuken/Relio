using Microsoft.EntityFrameworkCore;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Data.Configurations;
using Relio.Domain;

namespace Relio.Data.People;

/// <summary>
/// EF Core-backed implementation of <see cref="IRelationshipTypeService"/>. Lives in Relio.Data
/// like <see cref="PeopleService"/>, and follows the same rules: every query is explicitly
/// filtered by <see cref="IOwnedEntity.OwnerId"/>, reads are untracked and every mutation loads
/// what it changes tracked, saves once and clears the change tracker in a <c>finally</c>, because
/// the scoped <see cref="RelioDbContext"/> can live as long as a Blazor circuit (see
/// <see cref="PeopleService"/>'s remarks). Calls run in the context's <see cref="Concurrency.DatabaseLane"/>.
/// Never logs a type's name.
/// </summary>
public sealed class RelationshipTypeService(RelioDbContext dbContext, ICurrentUser currentUser) : IRelationshipTypeService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<RelationshipType>> ListAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        return await dbContext.RelationshipTypes
            .AsNoTracking()
            .Where(t => t.OwnerId == ownerId)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RelationshipTypeUsage>> ListWithUsageAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        // One query: the correlated count is served by the foreign key's index on People.
        return await dbContext.RelationshipTypes
            .AsNoTracking()
            .Where(t => t.OwnerId == ownerId)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .ThenBy(t => t.Id)
            .Select(t => new RelationshipTypeUsage(
                t.Id,
                t.Name,
                t.SortOrder,
                dbContext.People.Count(p => p.OwnerId == ownerId && p.RelationshipTypeId == t.Id)))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RelationshipType> CreateAsync(string? name, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();
        var normalized = NormalizeAndValidate(name);

        try
        {
            var existing = await dbContext.RelationshipTypes
                .AsNoTracking()
                .Where(t => t.OwnerId == ownerId)
                .Select(t => new { t.Name, t.SortOrder })
                .ToListAsync(cancellationToken);

            if (existing.Any(t => LabelNameRules.Comparer.Equals(t.Name, normalized)))
            {
                throw new LabelValidationException(LabelValidationError.NameTaken);
            }

            var type = new RelationshipType
            {
                OwnerId = ownerId,
                Name = normalized,
                SortOrder = existing.Count == 0 ? 0 : existing.Max(t => t.SortOrder) + 1,
            };
            dbContext.RelationshipTypes.Add(type);
            await SaveRefusingDuplicateNameAsync(cancellationToken);
            return type;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> RenameAsync(Guid relationshipTypeId, string? name, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();
        var normalized = NormalizeAndValidate(name);

        try
        {
            var type = await dbContext.RelationshipTypes
                .FirstOrDefaultAsync(t => t.Id == relationshipTypeId && t.OwnerId == ownerId, cancellationToken);
            if (type is null)
            {
                return false;
            }

            if (string.Equals(type.Name, normalized, StringComparison.Ordinal))
            {
                return true;
            }

            // Other types only: renaming "friend" to "Friend" is the same row, not a clash.
            var otherNames = await dbContext.RelationshipTypes
                .AsNoTracking()
                .Where(t => t.OwnerId == ownerId && t.Id != relationshipTypeId)
                .Select(t => t.Name)
                .ToListAsync(cancellationToken);
            if (otherNames.Any(other => LabelNameRules.Comparer.Equals(other, normalized)))
            {
                throw new LabelValidationException(LabelValidationError.NameTaken);
            }

            type.Name = normalized;
            await SaveRefusingDuplicateNameAsync(cancellationToken);
            return true;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid relationshipTypeId, Guid? reassignToId, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        if (reassignToId == relationshipTypeId)
        {
            throw new ArgumentException(
                "People cannot be moved to the relationship type being removed.", nameof(reassignToId));
        }

        try
        {
            var type = await dbContext.RelationshipTypes
                .FirstOrDefaultAsync(t => t.Id == relationshipTypeId && t.OwnerId == ownerId, cancellationToken);
            if (type is null)
            {
                return false;
            }

            // The target is a foreign id: it must be one of this user's types. Checked only after
            // the primary entity was found, so "no such type" and "not yours" never differ.
            if (reassignToId is { } targetId
                && !await dbContext.RelationshipTypes
                    .AnyAsync(t => t.Id == targetId && t.OwnerId == ownerId, cancellationToken))
            {
                throw new ForeignEntityNotOwnedException(ForeignEntityNames.RelationshipTypes);
            }

            // Tracked, one by one, rather than ExecuteUpdate: the InMemory provider used by the unit
            // tests has no ExecuteUpdate, and neither does it clear untracked dependents when the
            // type is removed. The people are changed first, then the type removed, and everything
            // goes in one save (one transaction on SQL Server). A person who was given this type in
            // the meantime is cleared by the foreign key's ON DELETE SET NULL.
            var people = await dbContext.People
                .Where(p => p.OwnerId == ownerId && p.RelationshipTypeId == relationshipTypeId)
                .ToListAsync(cancellationToken);
            foreach (var person in people)
            {
                person.RelationshipTypeId = reassignToId;
            }

            dbContext.RelationshipTypes.Remove(type);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    private static string NormalizeAndValidate(string? name)
    {
        var normalized = LabelNameRules.Normalize(name);
        if (LabelNameRules.Validate(normalized, RelationshipType.NameMaxLength) is { } error)
        {
            throw new LabelValidationException(error);
        }

        return normalized!;
    }

    /// <summary>
    /// Saves, translating the unique <c>(OwnerId, Name)</c> index into
    /// <see cref="LabelValidationError.NameTaken"/>: another request took the same name between
    /// reading the user's types and now. The database error quotes the duplicate key, so it is
    /// dropped, not attached.
    /// </summary>
    private async Task SaveRefusingDuplicateNameAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (SqlServerErrors.IsUniqueIndexViolation(exception, RelationshipTypeConfiguration.NameIndexName))
        {
            throw new LabelValidationException(LabelValidationError.NameTaken);
        }
    }
}
