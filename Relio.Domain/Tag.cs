namespace Relio.Domain;

/// <summary>
/// A short label the user can attach to one or more <see cref="Person"/> records. Minimal by
/// design (issue #10): it exists to exercise foreign-id ownership validation end to end (a
/// person can only be tagged with tags the same user owns), not to be a full tagging feature.
/// </summary>
public sealed class Tag : OwnedEntity
{
    /// <summary>The tag's display text, e.g. "family" or "college friend".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The people this tag is attached to. Always within the same owner.</summary>
    public ICollection<Person> People { get; set; } = new List<Person>();
}
