using Relio.Application.People;
using Relio.Domain;

namespace Relio.Web.Components.People;

/// <summary>The input of one contact method row a validation message is shown under.</summary>
public enum ContactMethodField
{
    /// <summary>The kind select.</summary>
    Kind,

    /// <summary>The optional label.</summary>
    Label,

    /// <summary>The value (the address, number, handle).</summary>
    Value,
}

/// <summary>
/// Words the <see cref="ContactMethodValidationError"/> codes Application reports: which input of
/// the row each belongs under and what it says. Same voice as <see cref="PersonFormMessages"/>:
/// what happened and how to fix it, without apologising.
/// </summary>
public static class ContactMethodFormMessages
{
    /// <summary>Returns the input and the message for <paramref name="error"/> on a row of <paramref name="kind"/>.</summary>
    public static (ContactMethodField Field, string Message) For(ContactMethodValidationError error, ContactMethodKind kind) => error switch
    {
        ContactMethodValidationError.KindUnknown => (ContactMethodField.Kind, "Choose what kind of contact this is."),
        ContactMethodValidationError.ValueRequired => (ContactMethodField.Value, RequiredMessage(kind)),
        ContactMethodValidationError.ValueTooLong => (ContactMethodField.Value, "Keep this to 300 characters or fewer."),
        ContactMethodValidationError.LabelTooLong => (ContactMethodField.Label, "Keep the label to 50 characters or fewer."),
        ContactMethodValidationError.EmailInvalid => (ContactMethodField.Value, "Enter an email address like name@example.com."),
        ContactMethodValidationError.PhoneInvalidCharacters => (ContactMethodField.Value, "Use digits, spaces, a leading + and ( ) - . / only."),
        ContactMethodValidationError.PhoneDigitCount => (ContactMethodField.Value, "Enter a phone number with 3 to 15 digits."),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "No message is defined for this validation error."),
    };

    private static string RequiredMessage(ContactMethodKind kind) => kind switch
    {
        ContactMethodKind.Email => "Enter an email address, or remove this row.",
        ContactMethodKind.Phone => "Enter a phone number, or remove this row.",
        ContactMethodKind.Address => "Enter an address, or remove this row.",
        ContactMethodKind.Social => "Enter a handle or link, or remove this row.",
        _ => "Enter the details, or remove this row.",
    };
}
