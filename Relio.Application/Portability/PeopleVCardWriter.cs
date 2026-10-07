using System.Globalization;
using System.Text;
using Relio.Application.People;
using Relio.Domain;

namespace Relio.Application.Portability;

/// <summary>Writes contact-only vCard 4.0 files for a list of people.</summary>
public static class PeopleVCardWriter
{
    private const int MaxFoldedLineOctets = 75;

    /// <summary>Builds standards-compliant vCard text for the supplied people.</summary>
    public static string Write(IReadOnlyList<VCardPerson> people)
    {
        ArgumentNullException.ThrowIfNull(people);

        var builder = new StringBuilder();
        foreach (var person in people)
        {
            builder.Append("BEGIN:VCARD\r\nVERSION:4.0\r\n");
            AppendProperty(builder, "N", $"{Escape(person.LastName ?? string.Empty)};{Escape(person.FirstName)};;;", alreadyEscaped: true);
            AppendProperty(builder, "FN", string.Join(' ', new[] { person.FirstName, person.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))));

            if (!string.IsNullOrWhiteSpace(person.Nickname))
            {
                AppendProperty(builder, "NICKNAME", person.Nickname);
            }

            if (person.BirthdayDay is { } day && person.BirthdayMonth is { } month)
            {
                var birthday = person.BirthdayYear is { } year
                    ? new DateOnly(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : $"--{month:00}-{day:00}";
                AppendProperty(builder, "BDAY", birthday, alreadyEscaped: true);
            }

            var contactPosition = 0;
            foreach (var contact in person.ContactMethods.OrderBy(x => x.SortOrder))
            {
                var property = contact.Kind switch
                {
                    ContactMethodKind.Email => "EMAIL",
                    ContactMethodKind.Phone => "TEL",
                    ContactMethodKind.Address => "ADR",
                    ContactMethodKind.Social => "X-SOCIALPROFILE",
                    _ => null,
                };
                if (property is null || string.IsNullOrWhiteSpace(contact.Value))
                {
                    continue;
                }

                var group = contact.Label is null ? null : $"item{++contactPosition}";
                var value = contact.Kind == ContactMethodKind.Address
                    ? $";;{Escape(contact.Value)};;;;"
                    : Escape(contact.Value);

                var parameters = contact.Kind == ContactMethodKind.Phone ? ";VALUE=text" : string.Empty;
                AppendProperty(builder, $"{(group is null ? string.Empty : group + ".")}{property}{parameters}", value, alreadyEscaped: true);
                if (group is not null)
                {
                    AppendProperty(builder, $"{group}.X-ABLabel", $"_$!<{Escape(contact.Label!)}>!$_", alreadyEscaped: true);
                }
            }

            builder.Append("END:VCARD\r\n");
        }

        return builder.ToString();
    }

    private static void AppendProperty(StringBuilder builder, string property, string? value, bool alreadyEscaped = false)
    {
        var line = $"{property}:{(alreadyEscaped ? value : Escape(value ?? string.Empty))}";
        var run = new StringBuilder();
        var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var encoded = Encoding.UTF8.GetByteCount(rune.ToString());
            if (octets + encoded > MaxFoldedLineOctets)
            {
                builder.Append(run).Append("\r\n ");
                run.Clear();
                octets = 1;
            }

            run.Append(rune);
            octets += encoded;
        }

        builder.Append(run).Append("\r\n");
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal);
}
