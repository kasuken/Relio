using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Relio.Application.Ownership;
using Relio.Application.Paging;
using Relio.Application.People;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Data.Configurations;
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
/// construct this class directly, so the constructor must stay as it is. Calls run in the context's
/// <see cref="Concurrency.DatabaseLane"/> (registered by <c>AddDataService</c>), so loads started by
/// sibling components queue instead of colliding on the one shared context.
/// </para>
/// <para>
/// Nothing here logs a name, a nickname, a contact method, a tag or any profile text (see the
/// gdpr-compliant skill).
/// </para>
/// <para>
/// <b>Contact methods and tags (issue #24).</b> An update receives the person's whole list and
/// diffs it by id: matched rows are edited, rows missing from the request are removed, rows without
/// an id are added. Every foreign id (relationship type, tags, contact methods) is checked before
/// anything is mutated, so a bad one changes nothing. New children - contact methods and tags - are
/// added with <c>DbSet.Add</c> <b>explicitly</b>: <c>OwnedEntity</c> gives every instance a
/// non-empty <c>Guid</c> up front, so EF Core would otherwise find one merely reachable from a
/// tracked person and treat it as an existing row. It is last write wins: there is no concurrency
/// token, so a save from a stale tab replaces what is there, apart from the stale-id check above.
/// </para>
/// </remarks>
public sealed class PeopleService(RelioDbContext dbContext, ICurrentUser currentUser, TimeProvider timeProvider) : IPeopleService
{
    /// <inheritdoc />
    public async Task<Person?> GetAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        // Filtered includes: tags by name, contact methods in the user's order (and, like every
        // query, only the owner's). Two collections in one query is deliberate - see
        // AddRelioData, which states the SingleQuery behaviour for SQL Server.
        return await dbContext.People
            .AsNoTracking()
            .Include(p => p.Tags.OrderBy(t => t.Name))
            .Include(p => p.ContactMethods.Where(c => c.OwnerId == ownerId).OrderBy(c => c.SortOrder))
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
    public async Task<IReadOnlyList<PossibleDuplicate>> FindPossibleDuplicatesAsync(
        PossibleDuplicateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var ownerId = currentUser.RequireUserId();

        var probe = DuplicateProbe.Create(query);
        if (probe.First.Length == 0 && probe.EmailKeys.Count == 0 && probe.PhoneKeys.Count == 0)
        {
            return [];
        }

        // Read-only and untracked, so there is nothing to clear. The names are matched in memory and
        // never persisted, cached or logged (see PossibleDuplicateMatcher).
        var candidates = await DuplicateCandidateLoader.LoadAsync(dbContext, ownerId, probe, cancellationToken);
        return PossibleDuplicateMatcher.Find(probe, candidates, query.ExcludePersonId);
    }

    /// <inheritdoc />
    public async Task<Person> CreateAsync(CreatePersonRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = currentUser.RequireUserId();
        ThrowIfRepeatedContactMethodIds(request.ContactMethods);

        await ValidateAsync(ownerId, request, cancellationToken);

        try
        {
            await EnsureOwnedRelationshipTypeAsync(ownerId, request.RelationshipTypeId, cancellationToken);
            var tags = await ResolveTagsAsync(ownerId, request.TagIds, request.NewTagNames, cancellationToken);

            // A new person has no contact methods to edit, so any id is somebody else's (or invented).
            if (request.ContactMethods?.Any(c => c.Id is not null) == true)
            {
                throw new ForeignEntityNotOwnedException(ForeignEntityNames.ContactMethods);
            }

            // The person and its contact methods are built by PersonEntityBuilder, shared with the import.
            var person = PersonEntityBuilder.NewPerson(ownerId, request);

            foreach (var tag in tags.All)
            {
                person.Tags.Add(tag);
            }

            // Add traverses the whole graph, so the new person, its new contact methods and any new
            // tags are all inserted; tags that already exist are tracked Unchanged and only get a
            // join row. (An *update* must add new children explicitly - see UpdateAsync.)
            dbContext.People.Add(person);
            await SaveChangesAsync(tags.Created.Count > 0, cancellationToken);

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
        ThrowIfRepeatedContactMethodIds(request.ContactMethods);

        // Depends only on the request, so it says nothing about whether the person exists.
        await ValidateAsync(ownerId, request, cancellationToken);

        try
        {
            var person = await dbContext.People
                .Include(p => p.Tags)
                .Include(p => p.ContactMethods.Where(c => c.OwnerId == ownerId))
                .FirstOrDefaultAsync(p => p.Id == personId && p.OwnerId == ownerId, cancellationToken);
            if (person is null)
            {
                return false;
            }

            // Check every foreign id before mutating anything (and before creating any tag), so a
            // bad id leaves the existing person - and the user's tags - untouched.
            await EnsureOwnedRelationshipTypeAsync(ownerId, request.RelationshipTypeId, cancellationToken);
            var tags = await ResolveTagsAsync(ownerId, request.TagIds, request.NewTagNames, cancellationToken);

            // Every contact method id must be one of THIS person's, which covers another user's,
            // another person's of the same user, one that never existed - and one deleted in
            // another tab while this form was open. All four read the same.
            var existing = person.ContactMethods.ToDictionary(c => c.Id);
            var contactMethods = request.ContactMethods ?? [];
            if (contactMethods.Any(c => c.Id is Guid id && !existing.ContainsKey(id)))
            {
                throw new ForeignEntityNotOwnedException(ForeignEntityNames.ContactMethods);
            }

            PersonEntityBuilder.ApplyProfile(person, request);

            person.Tags.Clear();
            foreach (var tag in tags.All)
            {
                person.Tags.Add(tag);
            }

            // New tags go into the context explicitly. OwnedEntity gives every instance a non-empty
            // Guid up front, so a new tag merely reachable from the tracked person would be
            // discovered as an existing row (Modified), and the save would fail with a concurrency
            // exception for an UPDATE that touches no row. The same goes for new contact methods.
            foreach (var tag in tags.Created)
            {
                dbContext.Tags.Add(tag);
            }

            var kept = new HashSet<Guid>();
            for (var position = 0; position < contactMethods.Count; position++)
            {
                var input = contactMethods[position];
                if (input.Id is Guid id)
                {
                    PersonEntityBuilder.ApplyContactMethod(existing[id], input, position);
                    kept.Add(id);
                }
                else
                {
                    var added = PersonEntityBuilder.CreateContactMethod(ownerId, input, position);
                    added.PersonId = person.Id;
                    dbContext.ContactMethods.Add(added);
                }
            }

            foreach (var gone in existing.Values.Where(c => !kept.Contains(c.Id)))
            {
                dbContext.ContactMethods.Remove(gone);
            }

            await SaveChangesAsync(tags.Created.Count > 0, cancellationToken);
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

    /// <inheritdoc />
    /// <remarks>
    /// Loads the person (with the owner in the query, so another user's person is "not found" before
    /// anything else is touched), removes everything that belongs to them in
    /// <see cref="RemoveDependentsAsync"/>, removes the person, and saves <b>once</b> - a single
    /// transaction on SQL Server. No <c>ExecuteDelete</c> and no explicit transaction: the InMemory
    /// provider used by the unit tests supports neither. Nothing is logged, not even the id.
    /// </remarks>
    public async Task<bool> DeleteAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        try
        {
            // Tags are included so the join rows are tracked: clearing the collection deletes the
            // links (and only the links) when the person goes.
            var person = await dbContext.People
                .Include(p => p.Tags)
                .FirstOrDefaultAsync(p => p.Id == personId && p.OwnerId == ownerId, cancellationToken);
            if (person is null)
            {
                return false;
            }

            await RemoveDependentsAsync(ownerId, person, cancellationToken);
            dbContext.People.Remove(person);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Removes everything that belongs to <paramref name="person"/>, so deleting the person leaves
    /// nothing behind. Called only by <see cref="DeleteAsync"/>, before the person itself is removed
    /// and in the same save.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why explicit.</b> EF Core only cascades to dependents it is <i>tracking</i>, and the InMemory
    /// provider used by the unit tests enforces no foreign keys at all, so a child that is not removed
    /// here would be orphaned there and look fine. On SQL Server the <c>ON DELETE CASCADE</c> every
    /// child's foreign key carries is the backstop; <c>PersonDeleteSqlServerTests</c> proves every
    /// foreign key that references <c>People</c> cascades.
    /// </para>
    /// <para>
    /// <b>CHECKLIST - every new entity with a <c>PersonId</c> adds one line here, in the same pull
    /// request, and to <c>PersonDeleteChecklistTests</c>.</b> Always filter by <c>OwnerId</c> AND
    /// <c>PersonId</c>.
    /// <list type="bullet">
    /// <item><description>[x] <c>ContactMethods</c> (#24): removed below.</description></item>
    /// <item><description>[x] Tag links (the <c>PersonTags</c> join): <c>person.Tags.Clear()</c> deletes the join rows only. The <c>Tag</c> rows stay, and so do other people's links to them.</description></item>
    /// <item><description>[ ] Interactions (#31) and their participants (#35): remove this person's participant rows; delete an interaction only when this person is its last participant.</description></item>
    /// <item><description>[ ] Notes (#32).</description></item>
    /// <item><description>[ ] Reminders (#37, #38), if they are stored as rows. The cadence (#41) is a column on <c>Person</c>.</description></item>
    /// <item><description>[ ] Difficult moments (#43). Their status (#44) is a column.</description></item>
    /// </list>
    /// A table with two foreign keys to <c>People</c> cannot cascade both on SQL Server (multiple cascade
    /// paths): make one <c>NoAction</c> and delete those rows here. Account deletion (#59) deletes every
    /// owned table itself and does not go through this method.
    /// </para>
    /// </remarks>
    private async Task RemoveDependentsAsync(string ownerId, Person person, CancellationToken cancellationToken)
    {
        // The join rows only (the person's tags were loaded tracked): the Tag rows are the user's
        // and stay, as do other people's links to them.
        person.Tags.Clear();

        var contactMethods = await dbContext.ContactMethods
            .Where(c => c.OwnerId == ownerId && c.PersonId == person.Id)
            .ToListAsync(cancellationToken);
        dbContext.ContactMethods.RemoveRange(contactMethods);
    }

    /// <summary>
    /// Throws <see cref="PersonValidationException"/> when <paramref name="input"/> breaks a rule
    /// in <see cref="PersonProfileRules"/>. "Today" for the future-birthday rule is today in the
    /// user's own time zone.
    /// </summary>
    private async Task ValidateAsync(string ownerId, IPersonProfileInput input, CancellationToken cancellationToken)
    {
        var today = await UserToday.GetAsync(dbContext, ownerId, timeProvider, cancellationToken);

        var errors = PersonProfileRules.Validate(input, today);
        var contactMethodProblems = PersonProfileRules.ValidateContactMethods(input);
        if (errors.Count > 0 || contactMethodProblems.Count > 0)
        {
            throw new PersonValidationException(errors, contactMethodProblems);
        }
    }

    /// <summary>
    /// A request that names one contact method twice is malformed (no form produces it), not a
    /// validation problem a user can fix, so it is rejected loudly rather than letting the second
    /// row silently win. Says nothing about the person, so it is safe before any lookup.
    /// </summary>
    private static void ThrowIfRepeatedContactMethodIds(IReadOnlyList<ContactMethodInput>? contactMethods)
    {
        if (contactMethods is null)
        {
            return;
        }

        var ids = contactMethods.Where(c => c.Id is not null).Select(c => c.Id!.Value).ToList();
        if (ids.Count != ids.Distinct().Count())
        {
            throw new ArgumentException("A contact method can appear only once in a request.", nameof(contactMethods));
        }
    }

    /// <summary>
    /// Saves, translating the unique <c>(OwnerId, Name)</c> index on tags into a validation error
    /// when - and only when - this save is creating tags: another request took the same name
    /// between reading the user's tags and now. The person form reloads its tags and asks again.
    /// </summary>
    private async Task SaveChangesAsync(bool createdTags, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (createdTags && IsTagNameConflict(exception))
        {
            throw new PersonValidationException([PersonValidationError.TagNameConflict]);
        }
    }

    private static bool IsTagNameConflict(DbUpdateException exception) =>
        SqlServerErrors.IsUniqueIndexViolation(exception, TagConfiguration.NameIndexName);

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
            throw new ForeignEntityNotOwnedException(ForeignEntityNames.RelationshipTypes);
        }
    }

    /// <summary>
    /// Resolves the tags a request wants attached: the supplied ids (each must be one of
    /// <paramref name="ownerId"/>'s tags - otherwise <see cref="ForeignEntityNotOwnedException"/>,
    /// whether the tag does not exist or belongs to a different user, the two being
    /// indistinguishable to the caller) plus the supplied names. A name is matched against the
    /// owner's tags in code with <see cref="TagNameRules.Comparer"/> (the InMemory provider is
    /// case-sensitive, so the database cannot be trusted to) and attaches the tag it matches;
    /// otherwise a new <see cref="Tag"/> is built - not tracked, not saved - and returned in
    /// <see cref="TagResolution.Created"/> for the caller to add and save in the same unit of work.
    /// </summary>
    private async Task<TagResolution> ResolveTagsAsync(
        string ownerId,
        IReadOnlyCollection<Guid>? tagIds,
        IReadOnlyCollection<string>? newTagNames,
        CancellationToken cancellationToken)
    {
        var ids = tagIds?.Distinct().ToList() ?? [];
        var names = (newTagNames ?? [])
            .Select(TagNameRules.Normalize)
            .OfType<string>()
            .Distinct(TagNameRules.Comparer)
            .ToList();
        if (ids.Count == 0 && names.Count == 0)
        {
            return new TagResolution([], []);
        }

        // All of the owner's tags, tracked: at most a few hundred short rows, and it is what makes
        // a name match, an id check and an attach one lookup. (A tag that the person already has is
        // tracked already, and identity resolution hands back the same instance.)
        var owned = await dbContext.Tags
            .Where(t => t.OwnerId == ownerId)
            .ToListAsync(cancellationToken);
        var ownedById = owned.ToDictionary(t => t.Id);

        var attach = new Dictionary<Guid, Tag>();
        foreach (var id in ids)
        {
            if (!ownedById.TryGetValue(id, out var tag))
            {
                throw new ForeignEntityNotOwnedException(ForeignEntityNames.Tags);
            }

            attach[id] = tag;
        }

        var created = new List<Tag>();
        foreach (var name in names)
        {
            var match = owned.FirstOrDefault(t => TagNameRules.Comparer.Equals(t.Name, name));
            if (match is not null)
            {
                attach[match.Id] = match;
                continue;
            }

            var tag = new Tag { OwnerId = ownerId, Name = name };
            created.Add(tag);
            attach[tag.Id] = tag;
        }

        return new TagResolution([.. attach.Values], created);
    }

    /// <summary>The tags to attach, and the subset of them that does not exist yet.</summary>
    private sealed record TagResolution(IReadOnlyList<Tag> All, IReadOnlyList<Tag> Created);
}
