namespace Relio.Application.People.Import;

/// <summary>
/// One person to create from an import. It satisfies <see cref="IPersonProfileInput"/> so the same
/// <see cref="PersonProfileRules"/> and <see cref="ContactMethodRules"/> validate it as validate the
/// person form, but it carries <b>no foreign ids</b>: no relationship type and no tags (the import does
/// not set them), so there is nothing to ownership-check. Personal data: never log it.
/// </summary>
public sealed record ImportPersonRequest : IPersonProfileInput
{
    /// <summary>The first name. Required.</summary>
    public required string FirstName { get; init; }

    /// <summary>The last name.</summary>
    public string? LastName { get; init; }

    /// <summary>The nickname.</summary>
    public string? Nickname { get; init; }

    /// <summary>The day of the birthday.</summary>
    public int? BirthdayDay { get; init; }

    /// <summary>The month of the birthday.</summary>
    public int? BirthdayMonth { get; init; }

    /// <summary>The year of birth, when the file had one.</summary>
    public int? BirthdayYear { get; init; }

    /// <summary>The notes, stored as the person's details.</summary>
    public string? Details { get; init; }

    /// <summary>The contact methods, in order. Every <see cref="ContactMethodInput.Id"/> is <see langword="null"/>.</summary>
    public IReadOnlyList<ContactMethodInput> ContactMethods { get; init; } = [];

    /// <summary>An import never says how the user met someone.</summary>
    public string? HowWeMet => null;

    Guid? IPersonProfileInput.RelationshipTypeId => null;

    IReadOnlyCollection<Guid>? IPersonProfileInput.TagIds => null;

    IReadOnlyCollection<string>? IPersonProfileInput.NewTagNames => null;

    IReadOnlyList<ContactMethodInput>? IPersonProfileInput.ContactMethods => ContactMethods;
}
