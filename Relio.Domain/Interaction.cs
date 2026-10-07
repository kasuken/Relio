namespace Relio.Domain;

/// <summary>
/// A dated record of an interaction with one or more people. The user calendar date is stored
/// directly; the interaction is shared by its participant links rather than owned by one profile.
/// </summary>
public sealed class Interaction : OwnedEntity
{
    /// <summary>The maximum length of an interaction description, in characters.</summary>
    public const int DescriptionMaxLength = 10_000;

    /// <summary>The calendar date on which the interaction happened in the user's time zone.</summary>
    public DateOnly OccurredOn { get; set; }

    /// <summary>The kind of interaction.</summary>
    public InteractionKind Kind { get; set; }

    /// <summary>The user's private description of the interaction.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The people who took part in the interaction.</summary>
    public ICollection<InteractionParticipant> Participants { get; set; } = new List<InteractionParticipant>();
}
