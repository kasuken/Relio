namespace Relio.Domain;

/// <summary>
/// A short label the user can attach to one or more <see cref="Person"/> records. Introduced by
/// issue #10 to exercise foreign-id ownership validation end to end (a person can only be tagged
/// with tags the same user owns); created by typing a new name into the person form (issue #24)
/// and managed in settings (issue #25).
/// </summary>
public sealed class Tag : OwnedEntity
{
    /// <summary>The longest <see cref="Name"/> Relio stores, in characters.</summary>
    public const int NameMaxLength = 50;

    /// <summary>The tag's display text, e.g. "family" or "college friend".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The people this tag is attached to. Always within the same owner.</summary>
    public ICollection<Person> People { get; set; } = new List<Person>();
}
