using Microsoft.EntityFrameworkCore;
using Relio.Application.Ownership;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Data.Configurations;
using Relio.Domain;

namespace Relio.Data.People;

/// <summary>
/// EF Core implementation of <see cref="IPersonLinkService"/>. Every query filters by owner
/// explicitly; reads are untracked, and each mutation saves once and clears the circuit-scoped
/// context's tracker in a finally block. Nothing here logs a name or an id.
/// </summary>
/// <remarks>
/// <para>
/// A link never makes a person active, so this service does not take <c>PlanLimits</c>.
/// </para>
/// <para>
/// The ends of a row are normalized in .NET by <see cref="PersonLink.SetEnds"/>; the service never
/// compares ids in a query for ordering (SQL Server orders <c>uniqueidentifier</c> differently).
/// The "already linked" rule is checked in code because the InMemory provider used by the unit tests
/// has no unique index; on SQL Server the index (<see cref="PersonLinkConfiguration.PairIndexName"/>)
/// is the backstop for a race, and the exception it raises is never logged or attached because it
/// quotes the owner id.
/// </para>
/// </remarks>
public sealed class PersonLinkService(RelioDbContext dbContext, ICurrentUser currentUser) : IPersonLinkService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PersonConnection>> ListForPersonAsync(
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        // The owner filter is on the link, so another user's person gives nothing here.
        var links = await dbContext.PersonLinks
            .AsNoTracking()
            .Where(l => l.OwnerId == ownerId && (l.PersonId == personId || l.RelatedPersonId == personId))
            .Select(l => new { l.Id, l.PersonId, l.RelatedPersonId, l.Kind })
            .ToListAsync(cancellationToken);
        if (links.Count == 0)
        {
            return [];
        }

        var otherIds = links
            .Select(l => l.PersonId == personId ? l.RelatedPersonId : l.PersonId)
            .Distinct()
            .ToArray();

        // A projection of the name columns only: the encrypted narrative fields are never loaded.
        var others = await dbContext.People
            .AsNoTracking()
            .Where(p => p.OwnerId == ownerId && otherIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FirstName, p.LastName, p.IsArchived })
            .ToListAsync(cancellationToken);
        var othersById = others.ToDictionary(p => p.Id);

        var connections = new List<(string FirstName, string? LastName, PersonConnection Connection)>();
        foreach (var link in links)
        {
            var stub = new PersonLink { PersonId = link.PersonId, RelatedPersonId = link.RelatedPersonId, Kind = link.Kind };
            var otherId = stub.OtherPersonId(personId);
            if (!othersById.TryGetValue(otherId, out var other))
            {
                continue; // cannot happen with the foreign keys; never show a half-resolved row.
            }

            connections.Add((
                other.FirstName,
                other.LastName,
                new PersonConnection(
                    link.Id,
                    otherId,
                    Person.FormatDisplayName(other.FirstName, other.LastName),
                    other.IsArchived,
                    stub.RoleOfOther(personId))));
        }

        return connections
            .OrderBy(c => c.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.LastName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Connection.OtherPersonId)
            .Select(c => c.Connection)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(CreatePersonLinkRequest request, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();
        ArgumentNullException.ThrowIfNull(request);

        var errors = PersonLinkRules.Validate(request.PersonId, request.RelatedPersonId, request.Kind);
        if (errors.Count > 0)
        {
            throw new PersonLinkValidationException(errors);
        }

        try
        {
            // Both people must be the owner's. One query, one answer: a missing person and another
            // user's person look exactly the same.
            var people = await dbContext.People
                .AsNoTracking()
                .Where(p => p.OwnerId == ownerId && (p.Id == request.PersonId || p.Id == request.RelatedPersonId))
                .Select(p => new { p.Id, p.IsArchived })
                .ToListAsync(cancellationToken);
            if (people.Count != 2)
            {
                throw new ForeignEntityNotOwnedException(ForeignEntityNames.People);
            }

            if (people.Any(p => p.IsArchived))
            {
                throw new PersonLinkValidationException([PersonLinkValidationError.ArchivedPersonNotAllowed]);
            }

            var link = new PersonLink { OwnerId = ownerId };
            link.SetEnds(request.PersonId, request.RelatedPersonId, request.Kind!.Value);

            var alreadyLinked = await dbContext.PersonLinks
                .AsNoTracking()
                .AnyAsync(
                    l => l.OwnerId == ownerId && l.PersonId == link.PersonId && l.RelatedPersonId == link.RelatedPersonId,
                    cancellationToken);
            if (alreadyLinked)
            {
                throw new PersonLinkValidationException([PersonLinkValidationError.AlreadyLinked]);
            }

            dbContext.PersonLinks.Add(link);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
                when (SqlServerErrors.IsUniqueIndexViolation(exception, PersonLinkConfiguration.PairIndexName))
            {
                // Lost a race for the same pair. Not attached: the SqlException quotes the owner id.
                throw new PersonLinkValidationException([PersonLinkValidationError.AlreadyLinked]);
            }

            return link.Id;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        Guid linkId,
        UpdatePersonLinkRequest request,
        CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();
        ArgumentNullException.ThrowIfNull(request);

        var errors = PersonLinkRules.ValidateKind(request.Kind);
        if (errors.Count > 0)
        {
            throw new PersonLinkValidationException(errors);
        }

        try
        {
            var link = await dbContext.PersonLinks
                .FirstOrDefaultAsync(l => l.Id == linkId && l.OwnerId == ownerId, cancellationToken);
            if (link is null || !link.Involves(request.PersonId))
            {
                return false;
            }

            // The people stay; the kind is re-expressed from the requested side. A link to an
            // archived person can still be edited.
            link.SetEnds(request.PersonId, link.OtherPersonId(request.PersonId), request.Kind!.Value);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid linkId, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        try
        {
            var link = await dbContext.PersonLinks
                .FirstOrDefaultAsync(l => l.Id == linkId && l.OwnerId == ownerId, cancellationToken);
            if (link is null)
            {
                return false;
            }

            dbContext.PersonLinks.Remove(link);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }
}
