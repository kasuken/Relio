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

    public Task<bool> UpdateAsync(Guid personId, UpdatePersonRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> ArchiveAsync(Guid personId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> RestoreAsync(Guid personId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
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
