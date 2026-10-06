namespace Relio.Domain;

/// <summary>
/// One way to reach a <see cref="Person"/> - an email address, a phone number, an address, a social
/// handle - with an optional label such as "Work" or "Mobile" (issue #24). Owned by the same user
/// as the person, with its own <see cref="OwnedEntity.OwnerId"/> (see the "User-scoped data
/// pattern" in AGENTS.md); deleting the person deletes their contact methods.
/// </summary>
/// <remarks>
/// <para>
/// <b>Personal data.</b> Every value here is personal data about a third party. Never log a
/// <see cref="Value"/> or <see cref="NormalizedValue"/>, and never put one in a URL (see the
/// gdpr-compliant skill).
/// </para>
/// <para>
/// <see cref="NormalizedValue"/> is written only through
/// <c>Relio.Application.People.ContactMethodRules.ToNormalizedValue</c>, never by hand: it is the
/// comparison key issue #27 (duplicate detection) and issue #28 (merge) match on, so the rule that
/// produces it must be the one place it lives.
/// </para>
/// </remarks>
public sealed class ContactMethod : OwnedEntity
{
    /// <summary>The longest <see cref="Kind"/> name Relio stores (the column length), in characters.</summary>
    public const int KindMaxLength = 20;

    /// <summary>The longest <see cref="Label"/> Relio stores, in characters.</summary>
    public const int LabelMaxLength = 50;

    /// <summary>
    /// The longest <see cref="Value"/> Relio stores, in characters. 300 leaves room for a long
    /// postal address, and keeps the <c>(OwnerId, NormalizedValue)</c> index key (900 + 600 bytes)
    /// under SQL Server's 1,700-byte limit.
    /// </summary>
    public const int ValueMaxLength = 300;

    /// <summary>The longest <see cref="NormalizedValue"/> Relio stores, in characters. Never longer than the trimmed value it comes from.</summary>
    public const int NormalizedValueMaxLength = 300;

    /// <summary>The person this belongs to. Always one of the same owner's people.</summary>
    public Guid PersonId { get; set; }

    /// <summary>What sort of detail this is.</summary>
    public ContactMethodKind Kind { get; set; }

    /// <summary>An optional short label, like "Work" or "Mobile".</summary>
    public string? Label { get; set; }

    /// <summary>The detail as the user wrote it (trimmed). Personal data: never log it.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="Value"/> reduced to a comparison key (emails lower-cased, phone numbers to digits
    /// with a leading +, ...). See the remarks on the class.
    /// </summary>
    public string NormalizedValue { get; set; } = string.Empty;

    /// <summary>The position among the person's contact methods, from 0; the order the user arranged them in.</summary>
    public int SortOrder { get; set; }
}
