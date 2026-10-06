using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Relio.Application.Ownership;
using Relio.Application.Paging;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Domain;

namespace Relio.Data.People;

/// <summary>
/// EF Core-backed implementation of <see cref="IPeopleService"/>. Lives in Relio.Data (not
/// Relio.Application) because it depends on <see cref="RelioDbContext"/> directly, keeping the
/// Domain/Application layers free of EF Core; see the "User-scoped data pattern" section of
/// AGENTS.md for the rationale. Every query and mutation is explicitly filtered by
/// <see cref="IOwnedEntity.OwnerId"/> - there is no global query filter on the context.
/// </summary>
/// <remarks>
/// <para>
/// <b>The context can outlive a request.</b> <see cref="RelioDbContext"/> is scoped, and in an
/// interactive Blazor Server component a scope lives as long as the circuit - minutes or hours.
/// A tracked entity would then answer later reads from memory, hiding changes made elsewhere, and
/// a failed <c>SaveChanges</c> would leave an <i>Added</i> entity that the next save inserts again.
/// So reads are untracked (<c>AsNoTracking</c>), and every mutation loads what it changes tracked,
/// saves once, and clears the change tracker in a <c>finally</c> block. Not an
/// <c>IDbContextFactory</c>: ASP.NET Core Identity's stores need the scoped context, and tests
/// construct this class directly, so the constructor must stay as it is.
/// </para>
/// <para>
/// Nothing here logs a name, a nickname or any profile text (see the gdpr-compliant skill).
/// </para>
/// </remarks>
public sealed class PeopleService(RelioDbContext dbContext, ICurrentUser currentUser, TimeProvider timeProvider) : IPeopleService
{
    /// <inheritdoc />
    public async Task<Person?> GetAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        return await dbContext.People
            .AsNoTracking()
            .Include(p => p.Tags)
            .Include(p => p.RelationshipType)
            .FirstOrDefaultAsync(p => p.Id == personId && p.OwnerId == ownerId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Person>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var query = dbContext.People
            .AsNoTracking()
            .Include(p => p.RelationshipType)
            .Where(p => p.OwnerId == ownerId);
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
    /// <remarks>
    /// Two queries: one grouped count of everything the user has (the totals the page needs for its
    /// empty states and its count line, and the total that decides how many pages there are), then
    /// the page itself. A person added or archived between the two can make the count and the page
    /// disagree by a row until the next load, which is harmless. Every ordering ends with
    /// <c>Id</c> so a tie never reshuffles between pages, and the nulls-last order of
    /// <see cref="PeopleSort.LastContacted"/> is spelled out rather than left to the database
    /// (SQL Server happens to sort nulls first when ascending, other providers differ). Names and
    /// <c>DisplayName</c> are never logged, and <c>DisplayName</c> is not a column, so the queries
    /// order by the real ones.
    /// </remarks>
    public async Task<PeopleListResult> ListPageAsync(PeopleListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!Enum.IsDefined(query.Sort))
        {
            throw new ArgumentOutOfRangeException(nameof(query), "The sort order is not a known PeopleSort.");
        }

        var ownerId = currentUser.RequireUserId();
        var pageSize = Math.Clamp(query.PageSize, 1, PeopleListQuery.MaxPageSize);

        var counts = await dbContext.People
            .AsNoTracking()
            .Where(p => p.OwnerId == ownerId)
            .GroupBy(p => p.IsArchived)
            .Select(g => new { IsArchived = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var activeCount = counts.Where(c => !c.IsArchived).Sum(c => c.Count);
        var archivedCount = counts.Where(c => c.IsArchived).Sum(c => c.Count);

        var total = query.IncludeArchived ? activeCount + archivedCount : activeCount;
        if (total == 0)
        {
            return new PeopleListResult(new PagedResult<PersonListItem>([], 1, pageSize, 0), activeCount, archivedCount);
        }

        var pageCount = (total + pageSize - 1) / pageSize;
        var page = Math.Clamp(query.Page, 1, pageCount);

        var people = dbContext.People
            .AsNoTracking()
            .Where(p => p.OwnerId == ownerId);
        if (!query.IncludeArchived)
        {
            people = people.Where(p => !p.IsArchived);
        }

        var ordered = query.Sort switch
        {
            PeopleSort.Name => people
                .OrderBy(p => p.FirstName)
                .ThenBy(p => p.LastName)
                .ThenBy(p => p.Id),
            PeopleSort.RecentlyAdded => people
                .OrderByDescending(p => p.CreatedAtUtc)
                .ThenBy(p => p.Id),
            PeopleSort.LastContacted => people
                .OrderBy(p => p.LastContactedOn == null)
                .ThenByDescending(p => p.LastContactedOn)
                .ThenBy(p => p.FirstName)
                .ThenBy(p => p.LastName)
                .ThenBy(p => p.Id),
            _ => throw new UnreachableException(),
        };

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PersonListItem(
                p.Id,
                p.FirstName,
                p.LastName,
                p.RelationshipType != null ? p.RelationshipType.Name : null,
                p.LastContactedOn,
                p.IsArchived,
                p.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PeopleListResult(new PagedResult<PersonListItem>(items, page, pageSize, total), activeCount, archivedCount);
    }

    /// <inheritdoc />
    public async Task<Person> CreateAsync(CreatePersonRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = currentUser.RequireUserId();

        await ValidateAsync(ownerId, request, cancellationToken);

        try
        {
            await EnsureOwnedRelationshipTypeAsync(ownerId, request.RelationshipTypeId, cancellationToken);
            var tags = await ResolveOwnedTagsAsync(ownerId, request.TagIds, cancellationToken);

            var person = new Person { OwnerId = ownerId };
            ApplyProfile(person, request);

            foreach (var tag in tags)
            {
                person.Tags.Add(tag);
            }

            dbContext.People.Add(person);
            await dbContext.SaveChangesAsync(cancellationToken);

            return person;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(Guid personId, UpdatePersonRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = currentUser.RequireUserId();

        // Depends only on the request, so it says nothing about whether the person exists.
        await ValidateAsync(ownerId, request, cancellationToken);

        try
        {
            var person = await dbContext.People
                .Include(p => p.Tags)
                .FirstOrDefaultAsync(p => p.Id == personId && p.OwnerId == ownerId, cancellationToken);
            if (person is null)
            {
                return false;
            }

            // Resolve foreign ids before mutating the tracked person, so a bad id leaves the
            // existing person untouched.
            await EnsureOwnedRelationshipTypeAsync(ownerId, request.RelationshipTypeId, cancellationToken);
            var tags = await ResolveOwnedTagsAsync(ownerId, request.TagIds, cancellationToken);

            ApplyProfile(person, request);

            person.Tags.Clear();
            foreach (var tag in tags)
            {
                person.Tags.Add(tag);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> ArchiveAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        try
        {
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
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> RestoreAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        try
        {
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
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Throws <see cref="PersonValidationException"/> when <paramref name="input"/> breaks a rule
    /// in <see cref="PersonProfileRules"/>. "Today" for the future-birthday rule is today in the
    /// user's own time zone.
    /// </summary>
    private async Task ValidateAsync(string ownerId, IPersonProfileInput input, CancellationToken cancellationToken)
    {
        var today = await GetUserTodayAsync(ownerId, cancellationToken);

        var errors = PersonProfileRules.Validate(input, today);
        if (errors.Count > 0)
        {
            throw new PersonValidationException(errors);
        }
    }

    /// <summary>
    /// Today's date in <paramref name="ownerId"/>'s time zone. A missing profile or an
    /// unreadable stored zone falls back to UTC rather than refusing to save a person.
    /// </summary>
    private async Task<DateOnly> GetUserTodayAsync(string ownerId, CancellationToken cancellationToken)
    {
        var timeZoneId = await dbContext.UserProfiles
            .AsNoTracking()
            .Where(p => p.OwnerId == ownerId)
            .Select(p => p.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);

        var timeZone = TimeZoneIds.TryParse(timeZoneId, out var parsed) ? parsed : TimeZoneInfo.Utc;
        return UserCalendar.Today(timeProvider, timeZone);
    }

    /// <summary>Writes every profile field of <paramref name="input"/> onto <paramref name="person"/>, normalized.</summary>
    private static void ApplyProfile(Person person, IPersonProfileInput input)
    {
        person.FirstName = PersonProfileRules.NormalizeRequired(input.FirstName);
        person.LastName = PersonProfileRules.NormalizeOptional(input.LastName);
        person.Nickname = PersonProfileRules.NormalizeOptional(input.Nickname);
        person.RelationshipTypeId = input.RelationshipTypeId;
        person.HowWeMet = PersonProfileRules.NormalizeOptional(input.HowWeMet);
        person.Details = PersonProfileRules.NormalizeOptional(input.Details);

        // Validation guarantees day and month come together, and that a year never comes alone.
        var hasBirthday = input.BirthdayDay is not null && input.BirthdayMonth is not null;
        person.BirthdayDay = hasBirthday ? input.BirthdayDay : null;
        person.BirthdayMonth = hasBirthday ? input.BirthdayMonth : null;
        person.BirthdayYear = hasBirthday ? input.BirthdayYear : null;
    }

    /// <summary>
    /// Throws <see cref="ForeignEntityNotOwnedException"/> when <paramref name="relationshipTypeId"/>
    /// is not one of <paramref name="ownerId"/>'s relationship types - whether it does not exist
    /// or belongs to someone else, the two are indistinguishable to the caller. A null id (no
    /// relationship type) is fine.
    /// </summary>
    private async Task EnsureOwnedRelationshipTypeAsync(
        string ownerId,
        Guid? relationshipTypeId,
        CancellationToken cancellationToken)
    {
        if (relationshipTypeId is not Guid id)
        {
            return;
        }

        var owned = await dbContext.RelationshipTypes
            .AsNoTracking()
            .AnyAsync(t => t.Id == id && t.OwnerId == ownerId, cancellationToken);
        if (!owned)
        {
            throw new ForeignEntityNotOwnedException("relationship types");
        }
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
