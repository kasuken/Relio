using Microsoft.EntityFrameworkCore;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Domain;

namespace Relio.Data.People;

/// <summary>
/// EF Core-backed implementation of <see cref="IRelationshipTypeService"/>. Lives in Relio.Data
/// like <see cref="PeopleService"/>, and follows the same rules: every query is explicitly
/// filtered by <see cref="IOwnedEntity.OwnerId"/>, and reads are untracked because the scoped
/// <see cref="RelioDbContext"/> can live as long as a Blazor circuit (see
/// <see cref="PeopleService"/>'s remarks). Calls run in the context's <see cref="Concurrency.DatabaseLane"/>.
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
}
