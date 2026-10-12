namespace Relio.Domain;

/// <summary>
/// What one person is to another in a <see cref="PersonLink"/>. A fixed set, stored by <b>name</b>
/// (a string column), so the database is readable and re-ordering the members is harmless.
/// </summary>
/// <remarks>
/// This is not the per-owner <see cref="RelationshipType"/> list: that says what someone is
/// <i>to the user</i>, has no reciprocal wording ("parent of" / "child of"), and is editable. A link
/// kind says what two people are <i>to each other</i>, and every kind has a reciprocal
/// (<see cref="PersonLinkKinds.Reciprocal"/>). Declaration order is the display order.
/// Renaming a member, or adding one, needs a migration that rewrites the
/// <c>CK_PersonLinks_Kind</c> check constraint (see <c>PersonLinkConfiguration</c>).
/// </remarks>
public enum PersonLinkKind
{
    /// <summary>A partner or spouse. Symmetric.</summary>
    Partner,

    /// <summary>A former partner or ex. Symmetric.</summary>
    FormerPartner,

    /// <summary>A parent. The reciprocal is <see cref="Child"/>.</summary>
    Parent,

    /// <summary>A child. The reciprocal is <see cref="Parent"/>.</summary>
    Child,

    /// <summary>A brother or sister. Symmetric.</summary>
    Sibling,

    /// <summary>A grandparent. The reciprocal is <see cref="Grandchild"/>.</summary>
    Grandparent,

    /// <summary>A grandchild. The reciprocal is <see cref="Grandparent"/>.</summary>
    Grandchild,

    /// <summary>Another relative. Symmetric.</summary>
    Relative,

    /// <summary>A friend. Symmetric.</summary>
    Friend,

    /// <summary>A colleague. Symmetric.</summary>
    Colleague,

    /// <summary>A manager. The reciprocal is <see cref="DirectReport"/>.</summary>
    Manager,

    /// <summary>A direct report. The reciprocal is <see cref="Manager"/>.</summary>
    DirectReport,

    /// <summary>A neighbour. Symmetric.</summary>
    Neighbour,

    /// <summary>Anything else. Symmetric.</summary>
    Other,
}

/// <summary>Helpers over <see cref="PersonLinkKind"/>.</summary>
public static class PersonLinkKinds
{
    /// <summary>Every kind, in display order.</summary>
    public static IReadOnlyList<PersonLinkKind> All { get; } = Enum.GetValues<PersonLinkKind>();

    /// <summary>
    /// The kind seen from the other end of a link: if B is A's <see cref="PersonLinkKind.Parent"/>,
    /// then A is B's <see cref="PersonLinkKind.Child"/>. A symmetric kind is its own reciprocal.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not a defined member.</exception>
    public static PersonLinkKind Reciprocal(PersonLinkKind kind) => kind switch
    {
        PersonLinkKind.Parent => PersonLinkKind.Child,
        PersonLinkKind.Child => PersonLinkKind.Parent,
        PersonLinkKind.Grandparent => PersonLinkKind.Grandchild,
        PersonLinkKind.Grandchild => PersonLinkKind.Grandparent,
        PersonLinkKind.Manager => PersonLinkKind.DirectReport,
        PersonLinkKind.DirectReport => PersonLinkKind.Manager,
        PersonLinkKind.Partner
            or PersonLinkKind.FormerPartner
            or PersonLinkKind.Sibling
            or PersonLinkKind.Relative
            or PersonLinkKind.Friend
            or PersonLinkKind.Colleague
            or PersonLinkKind.Neighbour
            or PersonLinkKind.Other => kind,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), "Not a defined link kind."),
    };

    /// <summary>Whether the kind reads the same from both ends.</summary>
    public static bool IsSymmetric(PersonLinkKind kind) => Reciprocal(kind) == kind;
}
