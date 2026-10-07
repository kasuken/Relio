using Relio.Domain;

namespace Relio.Application.People.Import;

/// <summary>One contact detail as a parser read it, before any rule is applied. Personal data: never log it.</summary>
public sealed record ImportContactDraft(ContactMethodKind Kind, string? Label, string Value);

/// <summary>A birthday as a parser read it: always a month and a day, and a year only when the file really had one.</summary>
public sealed record ImportBirthdayDraft(int Month, int Day, int? Year);

/// <summary>
/// One person as a parser read it, before the profile and contact method rules are applied.
/// Personal data: it lives in memory for the length of one import and is never persisted or logged.
/// </summary>
/// <param name="RowNumber">The 1-based card number in a vCard file, or the record number in a CSV file (the header is record 1).</param>
/// <param name="FirstName">The first name, or <see langword="null"/> when the file gave none.</param>
/// <param name="LastName">The last name, or <see langword="null"/>.</param>
/// <param name="Nickname">The nickname, or <see langword="null"/>.</param>
/// <param name="Birthday">The birthday, or <see langword="null"/> when there is none or it could not be read.</param>
/// <param name="BirthdayUnreadable">Whether the file had a birthday that could not be read (so a warning is shown).</param>
/// <param name="Details">The note, with its line breaks, or <see langword="null"/>.</param>
/// <param name="ContactMethods">The contact details, in file order.</param>
public sealed record ImportPersonDraft(
    int RowNumber,
    string? FirstName,
    string? LastName,
    string? Nickname,
    ImportBirthdayDraft? Birthday,
    bool BirthdayUnreadable,
    string? Details,
    IReadOnlyList<ImportContactDraft> ContactMethods)
{
    /// <summary>Whether the draft holds nothing at all (such a card or record is skipped, not shown).</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(FirstName)
        && string.IsNullOrWhiteSpace(LastName)
        && string.IsNullOrWhiteSpace(Nickname)
        && Birthday is null
        && !BirthdayUnreadable
        && string.IsNullOrWhiteSpace(Details)
        && ContactMethods.Count == 0;
}

/// <summary>What a parser found in one file.</summary>
/// <param name="People">The drafts, in file order, at most <see cref="ImportLimits.MaxPeople"/>.</param>
/// <param name="TotalFound">How many non-empty people the file holds (more than <paramref name="People"/> holds when truncated).</param>
/// <param name="Truncated">Whether the file held more than <see cref="ImportLimits.MaxPeople"/> people.</param>
/// <param name="SkippedEmpty">How many cards or records held nothing and were skipped.</param>
public sealed record ImportReadResult(IReadOnlyList<ImportPersonDraft> People, int TotalFound, bool Truncated, int SkippedEmpty)
{
    /// <summary>Builds a result from every non-empty draft: keeps the first <see cref="ImportLimits.MaxPeople"/>.</summary>
    internal static ImportReadResult Create(IReadOnlyList<ImportPersonDraft> nonEmpty, int skippedEmpty) =>
        new(
            nonEmpty.Count > ImportLimits.MaxPeople ? [.. nonEmpty.Take(ImportLimits.MaxPeople)] : nonEmpty,
            nonEmpty.Count,
            nonEmpty.Count > ImportLimits.MaxPeople,
            skippedEmpty);
}
