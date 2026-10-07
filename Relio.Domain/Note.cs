namespace Relio.Domain;

/// <summary>
/// A private, user-written note about a person. It is kept until the user removes it or deletes
/// the person it belongs to.
/// </summary>
public sealed class Note : OwnedEntity
{
    /// <summary>The maximum number of characters in a note.</summary>
    public const int TextMaxLength = 10000;

    /// <summary>The id of the person this note is about.</summary>
    public Guid PersonId { get; set; }

    /// <summary>The person this note is about, when loaded.</summary>
    public Person Person { get; set; } = null!;

    /// <summary>The user's note text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Whether this note stays near the top of the person's profile.</summary>
    public bool IsPinned { get; set; }
}
