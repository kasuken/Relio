namespace Relio.Domain;

/// <summary>
/// A link between two of the owner's people, for example "Marco is Anna's parent". One row per
/// pair, shown on both profiles with the reciprocal wording.
/// </summary>
/// <remarks>
/// <para>
/// The row means <b><see cref="RelatedPersonId"/> is <see cref="PersonId"/>'s
/// <see cref="Kind"/></b>. The ends are normalized: <see cref="PersonId"/> is always the lower
/// <see cref="Guid"/> by <see cref="Guid.CompareTo(Guid)"/>, so linking the same pair from either
/// profile produces the identical row and the unique <c>(OwnerId, PersonId, RelatedPersonId)</c>
/// index forbids a pair twice. The order is decided in .NET only; the database never compares
/// ids (SQL Server orders <c>uniqueidentifier</c> differently). Always write the ends through
/// <see cref="SetEnds"/>, which re-normalizes and flips the kind to its reciprocal when it swaps.
/// </para>
/// <para>
/// There are no navigation properties on purpose: merge rewrites both foreign keys, and a
/// navigation would need fix-up. Queries join explicitly. <c>Person</c> has no collection of links.
/// </para>
/// </remarks>
public sealed class PersonLink : OwnedEntity
{
    /// <summary>The longest name of a <see cref="PersonLinkKind"/>, the column length.</summary>
    public const int KindMaxLength = 20;

    /// <summary>The first end: always the lower <see cref="Guid"/> of the two.</summary>
    public Guid PersonId { get; set; }

    /// <summary>The second end: always the higher <see cref="Guid"/> of the two.</summary>
    public Guid RelatedPersonId { get; set; }

    /// <summary>What <see cref="RelatedPersonId"/> is to <see cref="PersonId"/>.</summary>
    public PersonLinkKind Kind { get; set; }

    /// <summary>Whether the person is one of the two ends.</summary>
    public bool Involves(Guid personId) => PersonId == personId || RelatedPersonId == personId;

    /// <summary>The end that is not <paramref name="viewedPersonId"/>.</summary>
    /// <exception cref="ArgumentException">The person is not an end of this link.</exception>
    public Guid OtherPersonId(Guid viewedPersonId)
    {
        if (viewedPersonId == PersonId)
        {
            return RelatedPersonId;
        }

        if (viewedPersonId == RelatedPersonId)
        {
            return PersonId;
        }

        throw new ArgumentException("The person is not an end of this link.", nameof(viewedPersonId));
    }

    /// <summary>What the other end is to <paramref name="viewedPersonId"/>.</summary>
    /// <exception cref="ArgumentException">The person is not an end of this link.</exception>
    public PersonLinkKind RoleOfOther(Guid viewedPersonId)
    {
        if (viewedPersonId == PersonId)
        {
            return Kind;
        }

        if (viewedPersonId == RelatedPersonId)
        {
            return PersonLinkKinds.Reciprocal(Kind);
        }

        throw new ArgumentException("The person is not an end of this link.", nameof(viewedPersonId));
    }

    /// <summary>
    /// Sets both ends and the kind, normalizing the order. <paramref name="roleOfOther"/> is what
    /// <paramref name="otherPersonId"/> is to <paramref name="viewedPersonId"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Both ends are the same person.</exception>
    public void SetEnds(Guid viewedPersonId, Guid otherPersonId, PersonLinkKind roleOfOther)
    {
        var order = viewedPersonId.CompareTo(otherPersonId);
        if (order == 0)
        {
            throw new ArgumentException("A person cannot be linked to themselves.", nameof(otherPersonId));
        }

        if (order < 0)
        {
            PersonId = viewedPersonId;
            RelatedPersonId = otherPersonId;
            Kind = roleOfOther;
        }
        else
        {
            PersonId = otherPersonId;
            RelatedPersonId = viewedPersonId;
            Kind = PersonLinkKinds.Reciprocal(roleOfOther);
        }
    }
}
