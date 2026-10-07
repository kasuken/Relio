using Microsoft.EntityFrameworkCore;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Data.Configurations;
using Relio.Domain;

namespace Relio.Data.People;

/// <summary>
/// EF Core-backed implementation of <see cref="ITagService"/>. Like <see cref="RelationshipTypeService"/>
/// every query is explicitly filtered by <see cref="IOwnedEntity.OwnerId"/>, reads are untracked and
/// every mutation loads what it changes tracked, saves once and clears the change tracker in a
/// <c>finally</c>, because the scoped <see cref="RelioDbContext"/> can live as long as a Blazor
/// circuit (see <see cref="PeopleService"/>'s remarks). Never logs a tag name.
/// </summary>
public sealed class TagService(RelioDbContext dbContext, ICurrentUser currentUser) : ITagService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        return await dbContext.Tags
            .AsNoTracking()
            .Where(t => t.OwnerId == ownerId)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagUsage>> ListWithUsageAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        // One query: the count goes through the join table's index on the tag id.
        return await dbContext.Tags
            .AsNoTracking()
            .Where(t => t.OwnerId == ownerId)
            .OrderBy(t => t.Name)
            .ThenBy(t => t.Id)
            .Select(t => new TagUsage(t.Id, t.Name, t.People.Count(p => p.OwnerId == ownerId)))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Tag> CreateAsync(string? name, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();
        var normalized = NormalizeAndValidate(name);

        try
        {
            var existingNames = await dbContext.Tags
                .AsNoTracking()
                .Where(t => t.OwnerId == ownerId)
                .Select(t => t.Name)
                .ToListAsync(cancellationToken);

            if (existingNames.Any(existing => LabelNameRules.Comparer.Equals(existing, normalized)))
            {
                throw new LabelValidationException(LabelValidationError.NameTaken);
            }

            var tag = new Tag { OwnerId = ownerId, Name = normalized };
            dbContext.Tags.Add(tag);
            await SaveRefusingDuplicateNameAsync(cancellationToken);
            return tag;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> RenameAsync(Guid tagId, string? name, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();
        var normalized = NormalizeAndValidate(name);

        try
        {
            var tag = await dbContext.Tags
                .FirstOrDefaultAsync(t => t.Id == tagId && t.OwnerId == ownerId, cancellationToken);
            if (tag is null)
            {
                return false;
            }

            if (string.Equals(tag.Name, normalized, StringComparison.Ordinal))
            {
                return true;
            }

            // Other tags only: renaming "chess" to "Chess" is the same row, not a clash.
            var otherNames = await dbContext.Tags
                .AsNoTracking()
                .Where(t => t.OwnerId == ownerId && t.Id != tagId)
                .Select(t => t.Name)
                .ToListAsync(cancellationToken);
            if (otherNames.Any(other => LabelNameRules.Comparer.Equals(other, normalized)))
            {
                throw new LabelValidationException(LabelValidationError.NameTaken);
            }

            tag.Name = normalized;
            await SaveRefusingDuplicateNameAsync(cancellationToken);
            return true;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid tagId, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        try
        {
            // The people are loaded (tracked) so the InMemory provider, which only cascades
            // tracked dependents, drops the join rows too; the join table's cascading foreign keys
            // are the SQL Server backstop. Clearing the collection removes only the links - the
            // people are not modified.
            var tag = await dbContext.Tags
                .Include(t => t.People)
                .FirstOrDefaultAsync(t => t.Id == tagId && t.OwnerId == ownerId, cancellationToken);
            if (tag is null)
            {
                return false;
            }

            tag.People.Clear();
            dbContext.Tags.Remove(tag);
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
        if (LabelNameRules.Validate(normalized, Tag.NameMaxLength) is { } error)
        {
            throw new LabelValidationException(error);
        }

        return normalized!;
    }

    /// <summary>
    /// Saves, translating the unique <c>(OwnerId, Name)</c> index into
    /// <see cref="LabelValidationError.NameTaken"/>: another request took the same name between
    /// reading the user's tags and now. The database error quotes the duplicate key, so it is
    /// dropped, not attached.
    /// </summary>
    private async Task SaveRefusingDuplicateNameAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (SqlServerErrors.IsUniqueIndexViolation(exception, TagConfiguration.NameIndexName))
        {
            throw new LabelValidationException(LabelValidationError.NameTaken);
        }
    }
}
