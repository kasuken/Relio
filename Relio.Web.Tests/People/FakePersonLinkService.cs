using Relio.Application.People;
using Relio.Domain;

namespace Relio.Web.Tests.People;

/// <summary>
/// An in-memory <see cref="IPersonLinkService"/> for component tests. It records every call, applies the
/// same pure input rules as the real service (<see cref="PersonLinkRules"/>) and lets a test make the next
/// create throw, an update or delete report "not found", or the list fail.
/// </summary>
internal sealed class FakePersonLinkService : IPersonLinkService
{
    /// <summary>What <see cref="ListForPersonAsync"/> returns, whatever the person.</summary>
    public List<PersonConnection> Connections { get; } = [];

    public List<Guid> Listed { get; } = [];

    public List<CreatePersonLinkRequest> Created { get; } = [];

    public List<(Guid LinkId, UpdatePersonLinkRequest Request)> Updated { get; } = [];

    public List<Guid> Deleted { get; } = [];

    public Exception? ThrowOnNextCreate { get; set; }

    public Exception? ThrowOnList { get; set; }

    public bool UpdateResult { get; set; } = true;

    public bool DeleteResult { get; set; } = true;

    public Task<IReadOnlyList<PersonConnection>> ListForPersonAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        Listed.Add(personId);
        if (ThrowOnList is { } exception)
        {
            throw exception;
        }

        return Task.FromResult<IReadOnlyList<PersonConnection>>(Connections.ToArray());
    }

    public Task<Guid> CreateAsync(CreatePersonLinkRequest request, CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextCreate is { } exception)
        {
            ThrowOnNextCreate = null;
            throw exception;
        }

        var errors = PersonLinkRules.Validate(request.PersonId, request.RelatedPersonId, request.Kind);
        if (errors.Count > 0)
        {
            throw new PersonLinkValidationException(errors);
        }

        Created.Add(request);
        var id = Guid.NewGuid();
        Connections.Add(new PersonConnection(id, request.RelatedPersonId, "Linked person", false, request.Kind!.Value));
        return Task.FromResult(id);
    }

    public Task<bool> UpdateAsync(Guid linkId, UpdatePersonLinkRequest request, CancellationToken cancellationToken = default)
    {
        var errors = PersonLinkRules.ValidateKind(request.Kind);
        if (errors.Count > 0)
        {
            throw new PersonLinkValidationException(errors);
        }

        Updated.Add((linkId, request));
        if (UpdateResult)
        {
            var index = Connections.FindIndex(connection => connection.LinkId == linkId);
            if (index >= 0)
            {
                Connections[index] = Connections[index] with { Role = request.Kind!.Value };
            }
        }

        return Task.FromResult(UpdateResult);
    }

    public Task<bool> DeleteAsync(Guid linkId, CancellationToken cancellationToken = default)
    {
        Deleted.Add(linkId);
        if (DeleteResult)
        {
            Connections.RemoveAll(connection => connection.LinkId == linkId);
        }

        return Task.FromResult(DeleteResult);
    }
}
