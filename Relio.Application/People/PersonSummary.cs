using Relio.Domain;

namespace Relio.Application.People;

/// <summary>
/// Just enough of a person to pick them from a list: id, names and whether they are archived. Personal
/// data that is only ever shown back to its owner; never log it.
/// </summary>
public sealed record PersonSummary(Guid Id, string FirstName, string? LastName, bool IsArchived)
{
    /// <summary>The name to show, built the same way as <see cref="Person.DisplayName"/>.</summary>
    public string DisplayName => Person.FormatDisplayName(FirstName, LastName);
}
