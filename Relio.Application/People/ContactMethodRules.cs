using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using Relio.Domain;

namespace Relio.Application.People;

/// <summary>
/// The rules a contact method must meet and the one place its comparison key is computed, as pure
/// functions with no database and no current user (like <see cref="PersonProfileRules"/>). The
/// service normalizes and validates with these; <c>Relio.Web</c> only words the resulting codes.
/// Issues #27 (duplicate detection), #28 (merge) and #29 (import) reuse
/// <see cref="ToNormalizedValue"/> and <see cref="Validate"/>, so there is exactly one definition of
/// "the same email address" or "a valid phone number".
/// </summary>
/// <remarks>
/// The format checks are deliberately <b>plausibility</b> checks, not delivery guarantees: Relio
/// keeps what the user wrote, it does not contact anyone. They exist to catch a typo (a missing
/// <c>@</c>, a letter in a phone number) and to keep what ends up in a <c>mailto:</c> or
/// <c>tel:</c> link boring. Nothing here logs or returns a value.
/// </remarks>
public static partial class ContactMethodRules
{
    /// <summary>The most contact methods one person can have.</summary>
    public const int MaxPerPerson = 20;

    /// <summary>The fewest digits a phone number can have.</summary>
    public const int MinPhoneDigits = 3;

    /// <summary>The most digits a phone number can have (E.164's limit).</summary>
    public const int MaxPhoneDigits = 15;

    /// <summary>The longest email address (RFC 5321's path limit).</summary>
    private const int MaxEmailLength = 254;

    /// <summary>Trims a label; null or blank becomes <see langword="null"/>.</summary>
    public static string? NormalizeLabel(string? label) => PersonProfileRules.NormalizeOptional(label);

    /// <summary>
    /// Trims a value, turns <c>\r\n</c> (and a lone <c>\r</c>) into <c>\n</c> so a multi-line address
    /// stores the same whichever browser sent it, and turns null into the empty string (which then
    /// fails validation). The result is what is stored as <see cref="ContactMethod.Value"/>.
    /// </summary>
    public static string NormalizeValue(ContactMethodKind kind, string? value)
    {
        _ = kind; // The same for every kind today; the parameter keeps room for a kind that needs more.
        return value is null
            ? string.Empty
            : value.Trim().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    /// <summary>An email address as a comparison key: trimmed and lower-cased.</summary>
    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// A phone number as a comparison key: a leading <c>+</c> is kept, then ASCII digits only
    /// (<c>"+39 333 123-4567"</c> becomes <c>"+393331234567"</c>). A leading <c>00</c> is <b>not</b>
    /// rewritten to <c>+</c>: whether it means "international" depends on the country.
    /// </summary>
    public static string NormalizePhone(string? phone)
    {
        var trimmed = (phone ?? string.Empty).Trim();
        var builder = new StringBuilder(trimmed.Length);
        if (trimmed.StartsWith('+'))
        {
            builder.Append('+');
        }

        foreach (var character in trimmed)
        {
            if (IsAsciiDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reduces <paramref name="value"/> to the key two contact methods are compared by. Email: trimmed,
    /// lower-cased. Phone: see <see cref="NormalizePhone"/>. Social: leading <c>@</c>s removed,
    /// whitespace collapsed, lower-cased. Address and other: whitespace (newlines included)
    /// collapsed to single spaces, lower-cased. Idempotent, and never longer than the trimmed value,
    /// so it always fits <see cref="ContactMethod.NormalizedValueMaxLength"/> when the value fits
    /// <see cref="ContactMethod.ValueMaxLength"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined kind.</exception>
    public static string ToNormalizedValue(ContactMethodKind kind, string? value) => kind switch
    {
        ContactMethodKind.Email => NormalizeEmail(value),
        ContactMethodKind.Phone => NormalizePhone(value),
        ContactMethodKind.Social => Collapse((value ?? string.Empty).Trim().TrimStart('@')).ToLowerInvariant(),
        ContactMethodKind.Address or ContactMethodKind.Other => Collapse(value ?? string.Empty).ToLowerInvariant(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown contact method kind."),
    };

    /// <summary>Returns every rule <paramref name="input"/> breaks (empty when it is fine). Lengths are measured after trimming.</summary>
    public static IReadOnlyList<ContactMethodValidationError> Validate(ContactMethodInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!Enum.IsDefined(input.Kind))
        {
            // Nothing else can be judged without knowing the kind.
            return [ContactMethodValidationError.KindUnknown];
        }

        var errors = new List<ContactMethodValidationError>();

        if (NormalizeLabel(input.Label)?.Length > ContactMethod.LabelMaxLength)
        {
            errors.Add(ContactMethodValidationError.LabelTooLong);
        }

        var value = NormalizeValue(input.Kind, input.Value);
        if (value.Length == 0)
        {
            errors.Add(ContactMethodValidationError.ValueRequired);
        }
        else if (value.Length > ContactMethod.ValueMaxLength)
        {
            errors.Add(ContactMethodValidationError.ValueTooLong);
        }
        else
        {
            switch (input.Kind)
            {
                case ContactMethodKind.Email when !IsPlausibleEmail(value):
                    errors.Add(ContactMethodValidationError.EmailInvalid);
                    break;
                case ContactMethodKind.Phone:
                    ValidatePhone(value, errors);
                    break;
            }
        }

        return errors;
    }

    /// <summary>
    /// Validates a whole submitted list and reports each problem with the index of its row. A
    /// <see langword="null"/> list is "no contact methods" and has no problems.
    /// </summary>
    public static IReadOnlyList<ContactMethodProblem> ValidateAll(IReadOnlyList<ContactMethodInput>? inputs)
    {
        if (inputs is null)
        {
            return [];
        }

        var problems = new List<ContactMethodProblem>();
        for (var index = 0; index < inputs.Count; index++)
        {
            foreach (var error in Validate(inputs[index]))
            {
                problems.Add(new ContactMethodProblem(index, error));
            }
        }

        return problems;
    }

    private static void ValidatePhone(string value, List<ContactMethodValidationError> errors)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var allowed = IsAsciiDigit(character)
                || (character == '+' && index == 0)
                || character is ' ' or '-' or '.' or '(' or ')' or '/';
            if (!allowed)
            {
                errors.Add(ContactMethodValidationError.PhoneInvalidCharacters);
                return;
            }
        }

        var digits = value.Count(IsAsciiDigit);
        if (digits is < MinPhoneDigits or > MaxPhoneDigits)
        {
            errors.Add(ContactMethodValidationError.PhoneDigitCount);
        }
    }

    /// <summary>
    /// A plain <c>name@host.tld</c> address: no whitespace, at most 254 characters, parsed by
    /// <see cref="MailAddress"/> into exactly the same text (so <c>Ada &lt;a@b.com&gt;</c> is refused),
    /// and a host that has a dot inside it.
    /// </summary>
    private static bool IsPlausibleEmail(string value)
    {
        if (value.Length > MaxEmailLength || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (!MailAddress.TryCreate(value, out var address) || !string.Equals(address.Address, value, StringComparison.Ordinal))
        {
            return false;
        }

        var host = address.Host;
        return host.Contains('.', StringComparison.Ordinal)
            && !host.StartsWith('.')
            && !host.EndsWith('.');
    }

    private static string Collapse(string value) => WhitespaceRun().Replace(value, " ").Trim();

    private static bool IsAsciiDigit(char character) => character is >= '0' and <= '9';

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
