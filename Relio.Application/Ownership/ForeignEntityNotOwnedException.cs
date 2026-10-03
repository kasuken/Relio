namespace Relio.Application.Ownership;

/// <summary>
/// Thrown when a mutation supplies a foreign id (e.g. a tag id) that does not resolve to an
/// entity owned by the current user. Deliberately uses one generic message for both "does not
/// exist" and "belongs to another user": the two cases must stay indistinguishable to the caller
/// so a request can never be used to probe for the existence of another user's data.
/// </summary>
public sealed class ForeignEntityNotOwnedException(string entityName)
    : Exception($"One or more referenced {entityName} could not be found.");
