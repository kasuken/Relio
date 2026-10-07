namespace Relio.Application.Ownership;

/// <summary>
/// The kinds of entity a mutation can reference by id, as they appear in
/// <see cref="ForeignEntityNotOwnedException"/>. Constants rather than free strings so a throw
/// site and the code that reacts to it cannot drift apart.
/// </summary>
public static class ForeignEntityNames
{
    /// <summary>A person's relationship type.</summary>
    public const string RelationshipTypes = "relationship types";

    /// <summary>Tags attached to a person.</summary>
    public const string Tags = "tags";

    /// <summary>A person's contact methods.</summary>
    public const string ContactMethods = "contact methods";
}
