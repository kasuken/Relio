using Relio.Domain;

namespace Relio.Application.People;

/// <summary>Input for linking two people.</summary>
public sealed record CreatePersonLinkRequest
{
    /// <summary>The profile being viewed; the link is added from its side.</summary>
    public required Guid PersonId { get; init; }

    /// <summary>The other person; <see cref="Guid.Empty"/> means none was chosen.</summary>
    public required Guid RelatedPersonId { get; init; }

    /// <summary>What <see cref="RelatedPersonId"/> is to <see cref="PersonId"/>; null means none was chosen.</summary>
    public PersonLinkKind? Kind { get; init; }
}

/// <summary>Input for changing the kind of an existing link. The two people cannot change.</summary>
public sealed record UpdatePersonLinkRequest
{
    /// <summary>The profile the kind is expressed from; it must be one of the link's two ends.</summary>
    public required Guid PersonId { get; init; }

    /// <summary>What the other person is to <see cref="PersonId"/>; null means none was chosen.</summary>
    public PersonLinkKind? Kind { get; init; }
}

/// <summary>One connection as shown on a person's profile.</summary>
/// <param name="LinkId">The id of the link row.</param>
/// <param name="OtherPersonId">The person at the other end.</param>
/// <param name="OtherDisplayName">The other person's display name.</param>
/// <param name="OtherIsArchived">Whether the other person is archived.</param>
/// <param name="Role">What the other person is to the viewed person (already the right way round).</param>
public sealed record PersonConnection(
    Guid LinkId,
    Guid OtherPersonId,
    string OtherDisplayName,
    bool OtherIsArchived,
    PersonLinkKind Role);
