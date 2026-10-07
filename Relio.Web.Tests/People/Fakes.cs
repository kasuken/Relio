using Relio.Application.Paging;
using Relio.Application.People;
using Relio.Domain;

namespace Relio.Web.Tests.People;

/// <summary>
/// In-memory <see cref="IPeopleService"/> for component tests: serves the people a test puts in
/// <see cref="Known"/>, records what was created, and can be told to throw once on the next create.
/// </summary>
internal sealed class FakePeopleService : IPeopleService
{
    public List<Person> Known { get; } = [];

    public List<CreatePersonRequest> Created { get; } = [];

    /// <summary>The id the next created person gets.</summary>
    public Guid NextId { get; set; } = Guid.NewGuid();

    /// <summary>Thrown by (and then cleared from) the next <see cref="CreateAsync"/> call.</summary>
    public Exception? ThrowOnNextCreate { get; set; }

    public Task<Person?> GetAsync(Guid personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Known.FirstOrDefault(p => p.Id == personId));

    public Task<IReadOnlyList<Person>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Person>>(Known.Where(p => includeArchived || !p.IsArchived).ToList());

    /// <summary>Every query <see cref="ListPageAsync"/> was called with, in order.</summary>
    public List<PeopleListQuery> ListPageQueries { get; } = [];

    /// <summary>What <see cref="ListPageAsync"/> returns; when unset, a page built from <see cref="Known"/>.</summary>
    public Func<PeopleListQuery, PeopleListResult>? ListPageResult { get; set; }

    public Task<PeopleListResult> ListPageAsync(PeopleListQuery query, CancellationToken cancellationToken = default)
    {
        ListPageQueries.Add(query);
        if (ListPageResult is { } result)
        {
            return Task.FromResult(result(query));
        }

        var items = Known
            .Where(p => query.IncludeArchived || !p.IsArchived)
            .Select(p => new PersonListItem(
                p.Id, p.FirstName, p.LastName, p.RelationshipType?.Name, p.LastContactedOn, p.IsArchived, p.CreatedAtUtc))
            .ToList();
        var page = new PagedResult<PersonListItem>(items, 1, query.PageSize, items.Count);
        return Task.FromResult(new PeopleListResult(
            page, Known.Count(p => !p.IsArchived), Known.Count(p => p.IsArchived)));
    }

    public Task<Person> CreateAsync(CreatePersonRequest request, CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextCreate is { } exception)
        {
            ThrowOnNextCreate = null;
            throw exception;
        }

        Created.Add(request);
        var person = new Person { Id = NextId, FirstName = request.FirstName.Trim() };
        Known.Add(person);
        return Task.FromResult(person);
    }

    /// <summary>Every update, with the id it was for, in order.</summary>
    public List<(Guid PersonId, UpdatePersonRequest Request)> Updated { get; } = [];

    /// <summary>What <see cref="UpdateAsync"/> returns: <see langword="false"/> means the person is gone.</summary>
    public bool UpdateResult { get; set; } = true;

    /// <summary>Thrown by (and then cleared from) the next <see cref="UpdateAsync"/> call.</summary>
    public Exception? ThrowOnNextUpdate { get; set; }

    public Task<bool> UpdateAsync(Guid personId, UpdatePersonRequest request, CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextUpdate is { } exception)
        {
            ThrowOnNextUpdate = null;
            throw exception;
        }

        Updated.Add((personId, request));
        return Task.FromResult(UpdateResult);
    }

    /// <summary>The ids <see cref="ArchiveAsync"/>, <see cref="RestoreAsync"/> and <see cref="DeleteAsync"/> were called with, in order.</summary>
    public List<Guid> Archived { get; } = [];

    public List<Guid> Restored { get; } = [];

    public List<Guid> Deleted { get; } = [];

    /// <summary>What each of them returns; <see langword="false"/> means the person is gone. On success they change <see cref="Known"/> like the real service.</summary>
    public bool ArchiveResult { get; set; } = true;

    public bool RestoreResult { get; set; } = true;

    public bool DeleteResult { get; set; } = true;

    /// <summary>The time an archive stamps on the person.</summary>
    public DateTime ArchiveTimeUtc { get; set; } = new(2026, 10, 6, 11, 30, 0, DateTimeKind.Utc);

    public Task<bool> ArchiveAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        Archived.Add(personId);
        if (ArchiveResult && Known.FirstOrDefault(p => p.Id == personId) is { } person && !person.IsArchived)
        {
            person.IsArchived = true;
            person.ArchivedAtUtc = ArchiveTimeUtc;
        }

        return Task.FromResult(ArchiveResult);
    }

    public Task<bool> RestoreAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        Restored.Add(personId);
        if (RestoreResult && Known.FirstOrDefault(p => p.Id == personId) is { } person)
        {
            person.IsArchived = false;
            person.ArchivedAtUtc = null;
        }

        return Task.FromResult(RestoreResult);
    }

    public Task<bool> DeleteAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        Deleted.Add(personId);
        if (DeleteResult)
        {
            Known.RemoveAll(p => p.Id == personId);
        }

        return Task.FromResult(DeleteResult);
    }
}

/// <summary>An <see cref="IRelationshipTypeService"/> that lists whatever a test sets, and counts the calls.</summary>
internal sealed class FakeRelationshipTypeService : IRelationshipTypeService
{
    public List<RelationshipType> Types { get; } = [];

    public int ListCalls { get; private set; }

    public Task<IReadOnlyList<RelationshipType>> ListAsync(CancellationToken cancellationToken = default)
    {
        ListCalls++;
        return Task.FromResult<IReadOnlyList<RelationshipType>>(Types.ToList());
    }
}

/// <summary>An <see cref="ITagService"/> that lists whatever a test sets, and counts the calls.</summary>
internal sealed class FakeTagService : ITagService
{
    public List<Tag> Tags { get; } = [];

    public int ListCalls { get; private set; }

    public Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken = default)
    {
        ListCalls++;
        return Task.FromResult<IReadOnlyList<Tag>>(Tags.ToList());
    }
}
