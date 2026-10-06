namespace Relio.Domain;

/// <summary>
/// How the user thinks of someone: "Family", "Friend", "Colleague". A per-user list rather than a
/// fixed enum, so each user can rename, add and remove their own (issue #25). Every user starts
/// with <see cref="DefaultNames"/>, seeded when the account is created - never lazily, so a
/// default the user deleted does not come back (see the "User-scoped data pattern" section of
/// AGENTS.md).
/// </summary>
public sealed class RelationshipType : OwnedEntity
{
    /// <summary>The longest <see cref="Name"/> Relio stores, in characters.</summary>
    public const int NameMaxLength = 50;

    /// <summary>
    /// The relationship types every new account starts with, in the order they are offered.
    /// The <c>AddPersonProfile</c> migration hard-codes its own copy of these names for accounts
    /// that predate the entity - changing a name here never rewrites existing rows.
    /// </summary>
    public static IReadOnlyList<string> DefaultNames { get; } =
        ["Family", "Partner", "Friend", "Colleague", "Acquaintance", "Other"];

    /// <summary>The type's display text, e.g. "Friend". Unique per owner (case-insensitively on SQL Server).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Where this type appears in the list the user picks from; lower comes first.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Creates one row per <see cref="DefaultNames"/> entry for the user <paramref name="ownerId"/>,
    /// in order. Every code path that creates a user must add these in the same save as the user's
    /// <see cref="UserProfile"/>.
    /// </summary>
    public static IReadOnlyList<RelationshipType> CreateDefaults(string ownerId)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);

        return DefaultNames
            .Select((name, index) => new RelationshipType { OwnerId = ownerId, Name = name, SortOrder = index })
            .ToList();
    }
}
