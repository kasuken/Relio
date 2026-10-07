using Relio.Domain;

namespace Relio.Application.People;

/// <summary>
/// What a profile looks like after a merge: every field resolved, the contact methods and tags
/// united. Built by <see cref="PersonMergeRules.Combine"/>, shown as the merge page's preview and
/// applied by <c>IPersonMergeService</c>, so the two can never disagree. It is also an
/// <see cref="IPersonProfileInput"/>, so <see cref="PersonProfileRules.Validate"/> checks the limits
/// the same way as for any other save. Personal data; never log it.
/// </summary>
public sealed record MergedProfile : IPersonProfileInput
{
    /// <inheritdoc />
    public string FirstName { get; init; } = string.Empty;

    /// <inheritdoc />
    public string? LastName { get; init; }

    /// <inheritdoc />
    public string? Nickname { get; init; }

    /// <inheritdoc />
    public Guid? RelationshipTypeId { get; init; }

    /// <inheritdoc />
    public int? BirthdayDay { get; init; }

    /// <inheritdoc />
    public int? BirthdayMonth { get; init; }

    /// <inheritdoc />
    public int? BirthdayYear { get; init; }

    /// <inheritdoc />
    public string? HowWeMet { get; init; }

    /// <inheritdoc />
    public string? Details { get; init; }

    /// <summary>Whether the merged profile is archived.</summary>
    public bool IsArchived { get; init; }

    /// <summary>When it was archived (UTC), or <see langword="null"/> while it is active.</summary>
    public DateTime? ArchivedAtUtc { get; init; }

    /// <summary>The later of the two last contacted dates, or <see langword="null"/> when neither person was ever contacted.</summary>
    public DateOnly? LastContactedOn { get; init; }

    /// <summary>Every contact method row of both people and what happens to it: the primary's rows first, then the duplicate's.</summary>
    public IReadOnlyList<ContactMethodMergeStep> ContactMethodSteps { get; init; } = [];

    /// <summary>The union of both people's tags: the primary's, then the duplicate's that the primary does not have.</summary>
    public IReadOnlyList<Tag> Tags { get; init; } = [];

    /// <summary>The rows the merged profile keeps (the primary's and the moved ones), as the person form would submit them.</summary>
    public IReadOnlyList<ContactMethodInput>? ContactMethods => ContactMethodSteps
        .Where(step => step.Action != ContactMethodMergeAction.DropDuplicate)
        .Select(step => new ContactMethodInput(step.Row.Id, step.Row.Kind, step.LabelToFill ?? step.Row.Label, step.Row.Value))
        .ToList();

    /// <inheritdoc />
    public IReadOnlyCollection<Guid>? TagIds => Tags.Select(tag => tag.Id).ToList();

    /// <summary>Always <see langword="null"/>: a merge never creates a tag.</summary>
    public IReadOnlyCollection<string>? NewTagNames => null;
}
