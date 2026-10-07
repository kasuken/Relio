namespace Relio.Application.People;

/// <summary>
/// Why one contact method row was rejected. Codes, not messages - <c>Relio.Web</c> words them next
/// to the row they belong to. Always paired with the row's index in a <see cref="ContactMethodProblem"/>.
/// </summary>
public enum ContactMethodValidationError
{
    /// <summary>The kind is not one of <c>ContactMethodKind</c>'s members.</summary>
    KindUnknown,

    /// <summary>The value is empty or whitespace.</summary>
    ValueRequired,

    /// <summary>The value is longer than <c>ContactMethod.ValueMaxLength</c>.</summary>
    ValueTooLong,

    /// <summary>The label is longer than <c>ContactMethod.LabelMaxLength</c>.</summary>
    LabelTooLong,

    /// <summary>An email address that is not a plain <c>name@host.tld</c> address.</summary>
    EmailInvalid,

    /// <summary>A phone number with a character other than digits, a leading +, spaces and ( ) - . /.</summary>
    PhoneInvalidCharacters,

    /// <summary>A phone number with fewer than 3 or more than 15 digits.</summary>
    PhoneDigitCount,
}
