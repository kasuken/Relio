using Relio.Application.People.Import;
using Relio.Web.Time;

namespace Relio.Web.Components.People.Import;

/// <summary>
/// Every word of the import page (issue #29), in Relio's voice: sentence case, calm, no exclamation
/// marks, "you" never "I". Kept out of the components so they are unit tested without rendering, and
/// so a test can prove every code Application defines has words. Application only ever returns codes.
/// </summary>
public static class ImportText
{
    /// <summary>The page's heading.</summary>
    public const string Title = "Import people";

    /// <summary>The line under the heading.</summary>
    public const string Intro = "Bring in people from your phone or another app. Choose a vCard (.vcf) or CSV file. Nothing is saved until you confirm.";

    /// <summary>What happens to the file.</summary>
    public const string PrivacyNote = "The file is read in your browser session and is not kept.";

    /// <summary>What can be imported.</summary>
    public const string Limits = "vCard or CSV, up to 1 MB and 2,000 people.";

    /// <summary>Shown while a file is being read.</summary>
    public const string Reading = "Reading the file";

    /// <summary>Shown while the people are being saved.</summary>
    public const string Importing = "Importing";

    /// <summary>The name shown for a person the file gave no name for.</summary>
    public const string Unnamed = "No name";

    /// <summary>The file chooser button.</summary>
    public const string ChooseFile = "Choose a file";

    /// <summary>The heading of the column matching step.</summary>
    public const string MapHeading = "Match the columns";

    /// <summary>The heading of the preview step.</summary>
    public const string PreviewHeading = "Choose who to import";

    /// <summary>Shown when the mapping has no name column.</summary>
    public const string NameColumnNeeded = "Choose the column with the first name or the full name.";

    /// <summary>Under the date order choice.</summary>
    public const string CheckBirthdays = "Check a birthday or two in the preview.";

    /// <summary>The label of the date order choice.</summary>
    public const string DatesWrittenAs = "Dates are written as";

    /// <summary>Said when the preview could not be imported because details changed meanwhile.</summary>
    public const string DetailsChanged = "Some details changed since the preview. Check the list and import again.";

    /// <summary>Said when the save failed.</summary>
    public const string ImportFailed = "The people couldn't be imported. Nothing was saved. Try again.";

    /// <summary>Said above the people who may already be in the list.</summary>
    public const string MayAlreadyBeInYourList = "May already be in your list:";

    /// <summary>Said when the file held more than the limit.</summary>
    public const string Truncated = "This file has more than 2,000 people. Only the first 2,000 are shown.";

    /// <summary>The words for a file that cannot be read at all.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="problem"/> is not a defined value.</exception>
    public static string FileProblem(ImportFileProblem problem) => problem switch
    {
        ImportFileProblem.Empty => "This file is empty. Choose another one.",
        ImportFileProblem.TooLarge => "This file is larger than 1 MB. Export fewer contacts, or split the file.",
        ImportFileProblem.NotContacts => "This doesn't look like a vCard or CSV file. Choose a .vcf or .csv file.",
        ImportFileProblem.Unreadable => "This file couldn't be read. Try exporting it again.",
        ImportFileProblem.NoPeople => "No people were found in this file.",
        _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "Not a known ImportFileProblem."),
    };

    /// <summary>The words for one problem with a row.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="problem"/> is not a defined value.</exception>
    public static string Problem(ImportProblem problem) => problem switch
    {
        ImportProblem.NameMissing => "No name, so this one can't be imported.",
        ImportProblem.FirstNameTooLong => "The first name is too long, so this one can't be imported.",
        ImportProblem.LastNameTooLong => "The last name is too long, so this one can't be imported.",
        ImportProblem.NotImportable => "Some details can't be imported, so this one is left out.",
        ImportProblem.NicknameTooLong => "The nickname is too long, so it's left out.",
        ImportProblem.DetailsTooLong => "The notes are too long, so they're left out.",
        ImportProblem.BirthdayUnreadable => "The birthday couldn't be read, so it's left out.",
        ImportProblem.BirthdayNotARealDate => "The birthday isn't a real date, so it's left out.",
        ImportProblem.BirthdayInTheFuture => "The birthday is in the future, so it's left out.",
        ImportProblem.TooManyContactMethods => "Only the first 20 contact details are kept.",
        ImportProblem.ContactValueTooLong => "A contact detail is too long, so it's left out.",
        ImportProblem.EmailKeptAsOther => "An email address didn't look right, so it's kept as other details.",
        ImportProblem.PhoneKeptAsOther => "A phone number didn't look right, so it's kept as other details.",
        _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "Not a known ImportProblem."),
    };

    /// <summary>The words for a kind of CSV file the headers looked like.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="preset"/> is not a defined value.</exception>
    public static string PresetName(CsvPreset preset) => preset switch
    {
        CsvPreset.Google => "Google Contacts export",
        CsvPreset.GoogleLegacy => "Google Contacts export (older format)",
        CsvPreset.Outlook => "Outlook export",
        CsvPreset.Generic => "Columns from the first row",
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Not a known CsvPreset."),
    };

    /// <summary>"Looks like a Google Contacts export." - the line above the column list.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="preset"/> is not a defined value.</exception>
    public static string LooksLike(CsvPreset preset) => preset switch
    {
        CsvPreset.Google => "Looks like a Google Contacts export.",
        CsvPreset.GoogleLegacy => "Looks like a Google Contacts export (older format).",
        CsvPreset.Outlook => "Looks like an Outlook export.",
        CsvPreset.Generic => "Matched the columns by the names in the first row.",
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Not a known CsvPreset."),
    };

    /// <summary>What a CSV column can be imported as.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="field"/> is not a defined value.</exception>
    public static string FieldName(CsvField field) => field switch
    {
        CsvField.Ignore => "Don't import",
        CsvField.FirstName => "First name",
        CsvField.MiddleName => "Middle name",
        CsvField.LastName => "Last name",
        CsvField.FullName => "Full name",
        CsvField.Nickname => "Nickname",
        CsvField.Birthday => "Birthday",
        CsvField.Email => "Email",
        CsvField.Phone => "Phone",
        CsvField.Address => "Address",
        CsvField.Notes => "Notes",
        CsvField.Label => "Label",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not a known CsvField."),
    };

    /// <summary>How dates are written.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="order"/> is not a defined value.</exception>
    public static string DateOrderName(DateOrder order) => order switch
    {
        DateOrder.YearMonthDay => "Year-month-day (1985-04-15)",
        DateOrder.DayMonthYear => "Day/month/year (15/04/1985)",
        DateOrder.MonthDayYear => "Month/day/year (04/15/1985)",
        _ => throw new ArgumentOutOfRangeException(nameof(order), order, "Not a known DateOrder."),
    };

    // Thousands separated by commas whatever the server's culture, like the other counts on the people pages.
    private static string Format(int count) => count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"12 people found. 10 selected."</summary>
    public static string Summary(ImportPreview preview, int selected)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var found = preview.Candidates.Count;
        var foundText = found == 1 ? "1 person found." : $"{Format(found)} people found.";
        var text = $"{foundText} {Format(selected)} selected.";
        return preview.SkippedEmpty > 0
            ? $"{text} {(preview.SkippedEmpty == 1 ? "1 empty entry was skipped." : $"{Format(preview.SkippedEmpty)} empty entries were skipped.")}"
            : text;
    }

    /// <summary>"Import 12 people".</summary>
    public static string ImportButton(int count) => count switch
    {
        0 => "Import people",
        1 => "Import 1 person",
        _ => $"Import {Format(count)} people",
    };

    /// <summary>"Imported 12 people".</summary>
    public static string Imported(int count) => count == 1 ? "Imported 1 person" : $"Imported {Format(count)} people";

    /// <summary>"Looks the same as row 3 in this file."</summary>
    public static string SameAsRow(int row) => $"Looks the same as row {row} in this file.";

    /// <summary>"Home address (5 columns)".</summary>
    public static string AddressBlockName(string? label, int columns) =>
        $"{(string.IsNullOrWhiteSpace(label) ? "Address" : label + " address")} ({columns} {(columns == 1 ? "column" : "columns")})";

    /// <summary>
    /// A person's details on one line: birthday, the first email, the first phone number and how many
    /// more details there are ("Birthday 15 April · ada@example.com · +44 7700 900123 · 2 more").
    /// Empty when there is nothing to say.
    /// </summary>
    public static string ContactSummary(ImportPersonRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parts = new List<string>();
        if (request.BirthdayMonth is int month && request.BirthdayDay is int day
            && Relio.Domain.Birthday.TryCreate(month, day, request.BirthdayYear, out var birthday))
        {
            parts.Add($"Birthday {BirthdayDisplay.Format(birthday)}");
        }

        var contacts = request.ContactMethods;
        var email = contacts.FirstOrDefault(contact => contact.Kind == Relio.Domain.ContactMethodKind.Email);
        var phone = contacts.FirstOrDefault(contact => contact.Kind == Relio.Domain.ContactMethodKind.Phone);
        if (email is not null)
        {
            parts.Add(email.Value ?? string.Empty);
        }

        if (phone is not null)
        {
            parts.Add(phone.Value ?? string.Empty);
        }

        var more = contacts.Count - (email is null ? 0 : 1) - (phone is null ? 0 : 1);
        if (more > 0)
        {
            parts.Add($"{more} more");
        }

        return string.Join(" · ", parts);
    }
}
