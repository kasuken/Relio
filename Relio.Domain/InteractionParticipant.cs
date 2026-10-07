namespace Relio.Domain;

/// <summary>
/// Links an interaction to one person in the same owner's private relationship memory.
/// </summary>
public sealed class InteractionParticipant : OwnedEntity
{
    /// <summary>The id of the interaction.</summary>
    public Guid InteractionId { get; set; }

    /// <summary>The interaction this participant belongs to.</summary>
    public Interaction Interaction { get; set; } = null!;

    /// <summary>The id of the participating person.</summary>
    public Guid PersonId { get; set; }

    /// <summary>The participating person.</summary>
    public Person Person { get; set; } = null!;
}
