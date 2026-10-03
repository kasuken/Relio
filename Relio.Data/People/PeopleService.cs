using Microsoft.EntityFrameworkCore;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Domain;

namespace Relio.Data.People;

/// <summary>
/// EF Core-backed implementation of <see cref="IPeopleService"/>. Lives in Relio.Data (not
/// Relio.Application) because it depends on <see cref="RelioDbContext"/> directly, keeping the
/// Domain/Application layers free of EF Core; see the "User-scoped data pattern" section of
/// AGENTS.md for the rationale. Every query and mutation is explicitly filtered by
/// <see cref="IOwnedEntity.OwnerId"/> - there is no global query filter on the context.
/// </summary>
public sealed class PeopleService(RelioDbContext dbContext, ICurrentUser currentUser, TimeProvider timeProvider) : IPeopleService
{
    /// <inheritdoc />
    public async Task<Person?> GetAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        return await dbContext.People
            .Include(p => p.Tags)
            .FirstOrDefaultAsync(p => p.Id == personId && p.OwnerId == ownerId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Person>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var query = dbContext.People.Where(p => p.OwnerId == ownerId);
        if (!includeArchived)
        {
            query = query.Where(p => !p.IsArchived);
        }

        return await query
            .OrderBy(p => p.FirstName)
            .ThenBy(p => p.LastName)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Person> CreateAsync(CreatePersonRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = currentUser.RequireUserId();

        var tags = await ResolveOwnedTagsAsync(ownerId, request.TagIds, cancellationToken);

        var person = new Person
        {
            OwnerId = ownerId,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Birthday = request.Birthday,
        };

        foreach (var tag in tags)
        {
            person.Tags.Add(tag);
        }

        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync(cancellationToken);

        return person;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(Guid personId, UpdatePersonRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = currentUser.RequireUserId();

        var person = await dbContext.People
            .Include(p => p.Tags)
            .FirstOrDefaultAsync(p => p.Id == personId && p.OwnerId == ownerId, cancellationToken);
        if (person is null)
        {
            return false;
        }

        // Resolve tags before mutating the tracked person, so a bad tag id leaves the existing
        // person untouched.
        var tags = await ResolveOwnedTagsAsync(ownerId, request.TagIds, cancellationToken);

        person.FirstName = request.FirstName;
        person.LastName = request.LastName;
        person.Birthday = request.Birthday;

        person.Tags.Clear();
        foreach (var tag in tags)
        {
            person.Tags.Add(tag);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ArchiveAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var person = await dbContext.People
            .FirstOrDefaultAsync(p => p.Id == personId && p.OwnerId == ownerId, cancellationToken);
        if (person is null)
        {
            return false;
        }

        if (!person.IsArchived)
        {
            person.IsArchived = true;
            person.ArchivedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> RestoreAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var person = await dbContext.People
            .FirstOrDefaultAsync(p => p.Id == personId && p.OwnerId == ownerId, cancellationToken);
        if (person is null)
        {
            return false;
        }

        if (person.IsArchived)
        {
            person.IsArchived = false;
            person.ArchivedAtUtc = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Resolves the supplied tag ids to tags owned by <paramref name="ownerId"/>. Throws
    /// <see cref="ForeignEntityNotOwnedException"/> if any id does not resolve - whether because
    /// the tag does not exist or because it belongs to a different user, the two are
    /// indistinguishable to the caller.
    /// </summary>
    private async Task<List<Tag>> ResolveOwnedTagsAsync(
        string ownerId,
        IReadOnlyCollection<Guid>? tagIds,
        CancellationToken cancellationToken)
    {
        if (tagIds is null || tagIds.Count == 0)
        {
            return [];
        }

        var distinctIds = tagIds.Distinct().ToList();

        var tags = await dbContext.Tags
            .Where(t => t.OwnerId == ownerId && distinctIds.Contains(t.Id))
            .ToListAsync(cancellationToken);

        if (tags.Count != distinctIds.Count)
        {
            throw new ForeignEntityNotOwnedException("tags");
        }

        return tags;
    }
}
