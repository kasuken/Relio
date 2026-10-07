namespace Relio.Application.Ownership;

/// <summary>
/// Thrown when a mutation supplies a foreign id (e.g. a tag id) that does not resolve to an
/// entity owned by the current user. Deliberately uses one generic message for both "does not
/// exist" and "belongs to another user": the two cases must stay indistinguishable to the caller
/// so a request can never be used to probe for the existence of another user's data.
/// </summary>
/// <param name="entityName">
/// Which kind of entity failed, one of <see cref="ForeignEntityNames"/>. It is not part of the
/// message's variable text beyond the kind, and never an id or a value.
/// </param>
public sealed class ForeignEntityNotOwnedException(string entityName)
    : Exception($"One or more referenced {entityName} could not be found.")
{
    /// <summary>
    /// The kind of entity that did not resolve (see <see cref="ForeignEntityNames"/>), so a form
    /// can say which of its several references went stale. The same words as in the message.
    /// </summary>
    public string EntityName { get; } = entityName;
}
