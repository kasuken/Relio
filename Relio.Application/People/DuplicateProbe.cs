using Relio.Domain;

namespace Relio.Application.People;

/// <summary>
/// A <see cref="PossibleDuplicateQuery"/> reduced to the keys it is matched by: the normalized
/// names and the email and phone keys worth comparing. Built with <see cref="Create"/>.
/// </summary>
/// <remarks>
/// <b>Personal data.</b> A probe is a copy of what the user typed. It lives in memory for the length
/// of one check (and, in the person form, as the <see cref="Key"/> string the warning was shown for);
/// never persist, cache or log it, and never put a part of it in an exception message, a URL or a
/// page title.
/// </remarks>
public sealed record DuplicateProbe
{
    /// <summary>The most email or phone keys one probe carries (the same as a person's contact method limit).</summary>
    public const int MaxKeys = ContactMethodRules.MaxPerPerson;

    private const char Separator = '\u001F';

    private DuplicateProbe()
    {
    }

    /// <summary>The normalized first name. Empty turns name matching off.</summary>
    public string First { get; private init; } = string.Empty;

    /// <summary>The normalized last name, or empty.</summary>
    public string Last { get; private init; } = string.Empty;

    /// <summary>The normalized nickname, or empty.</summary>
    public string Nickname { get; private init; } = string.Empty;

    /// <summary>The normalized full name (first and last normalized together).</summary>
    public string FullName { get; private init; } = string.Empty;

    /// <summary>Email keys: lower-cased, containing an <c>@</c>, distinct, at most <see cref="MaxKeys"/>.</summary>
    public IReadOnlyList<string> EmailKeys { get; private init; } = [];

    /// <summary>Phone keys (<see cref="ContactMethodRules.NormalizePhone"/>) with at least <see cref="ContactMethodRules.MinPhoneDigits"/> digits, distinct, at most <see cref="MaxKeys"/>.</summary>
    public IReadOnlyList<string> PhoneKeys { get; private init; } = [];

    /// <summary>
    /// Identifies the names being entered (full name and nickname), ignoring case, accents and
    /// spacing, and ignoring contact methods. The edit form uses it to tell a rename from any other change.
    /// </summary>
    public string NameKey => FullName + Separator + Nickname;

    /// <summary>
    /// Identifies everything that is compared: <see cref="NameKey"/> plus the sorted email and phone
    /// keys. Two probes with the same key find the same people, so a warning shown for one key and
    /// acknowledged need not be shown again for it.
    /// </summary>
    public string Key => string.Join(Separator, [NameKey, .. EmailKeys.Order(StringComparer.Ordinal), .. PhoneKeys.Order(StringComparer.Ordinal)]);

    /// <summary>
    /// The text a phone key is searched by in the database and compared on: the last eight digits when
    /// the number has at least eight, otherwise the whole key.
    /// </summary>
    public IReadOnlyList<string> PhoneSuffixes => PhoneKeys.Select(PossibleDuplicateMatcher.PhoneSuffix).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Reduces <paramref name="query"/> to its keys. A name longer than its column
    /// (<see cref="Person.FirstNameMaxLength"/> and so on) turns name matching off: the save would be
    /// refused anyway, and it bounds the work done per check.
    /// </summary>
    public static DuplicateProbe Create(PossibleDuplicateQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var namesFit = Fits(query.FirstName, Person.FirstNameMaxLength)
            && Fits(query.LastName, Person.LastNameMaxLength)
            && Fits(query.Nickname, Person.NicknameMaxLength);

        var first = namesFit ? PersonNameNormalizer.Normalize(query.FirstName) : string.Empty;
        var last = namesFit ? PersonNameNormalizer.Normalize(query.LastName) : string.Empty;
        var nickname = namesFit ? PersonNameNormalizer.Normalize(query.Nickname) : string.Empty;

        var emails = new List<string>();
        var phones = new List<string>();
        foreach (var method in query.ContactMethods ?? [])
        {
            if (method is null)
            {
                continue;
            }

            switch (method.Kind)
            {
                case ContactMethodKind.Email:
                    var email = ContactMethodRules.NormalizeEmail(method.Value);
                    if (email.Contains('@', StringComparison.Ordinal) && !emails.Contains(email, StringComparer.Ordinal))
                    {
                        emails.Add(email);
                    }

                    break;
                case ContactMethodKind.Phone:
                    var phone = ContactMethodRules.NormalizePhone(method.Value);
                    if (PossibleDuplicateMatcher.DigitCount(phone) >= ContactMethodRules.MinPhoneDigits && !phones.Contains(phone, StringComparer.Ordinal))
                    {
                        phones.Add(phone);
                    }

                    break;
            }
        }

        return new DuplicateProbe
        {
            First = first,
            Last = last,
            Nickname = nickname,
            FullName = first.Length == 0 ? string.Empty : PersonNameNormalizer.FullName(query.FirstName, query.LastName),
            EmailKeys = emails.Take(MaxKeys).ToList(),
            PhoneKeys = phones.Take(MaxKeys).ToList(),
        };
    }

    private static bool Fits(string? value, int maxLength) => PersonProfileRules.NormalizeRequired(value).Length <= maxLength;
}
