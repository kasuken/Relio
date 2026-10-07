namespace Relio.Application.People;

/// <summary>Why a person was reported as a possible duplicate. Declaration order is display order.</summary>
public enum PossibleDuplicateReason
{
    /// <summary>The full name is the same, ignoring case, accents, punctuation and spacing.</summary>
    SameName,

    /// <summary>The name is close: a small typo, a short form, a missing last name or a matching nickname.</summary>
    SimilarName,

    /// <summary>An email address is the same, ignoring case.</summary>
    SameEmail,

    /// <summary>A phone number is the same, or ends in the same eight digits (so a country code does not matter).</summary>
    SamePhone,
}
