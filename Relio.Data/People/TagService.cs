using Microsoft.EntityFrameworkCore;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Domain;

namespace Relio.Data.People;

/// <summary>
/// EF Core-backed implementation of <see cref="ITagService"/>. Like <see cref="RelationshipTypeService"/>
/// every query is explicitly filtered by <see cref="IOwnedEntity.OwnerId"/> and reads are untracked,
/// because the scoped <see cref="RelioDbContext"/> can live as long as a Blazor circuit (see
/// <see cref="PeopleService"/>'s remarks). Never logs a tag name.
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
}
