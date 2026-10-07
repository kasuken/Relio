namespace Relio.Application.People;

/// <summary>
/// Merges one of the current user's people (the duplicate) into another (the primary): the primary
/// keeps its id and everything recorded about the duplicate moves to it.
/// </summary>
public sealed record MergePeopleRequest
{
    /// <summary>The profile that stays. Its id, its creation time and its place in links survive.</summary>
    public required Guid PrimaryId { get; init; }

    /// <summary>The profile that is merged into the primary and removed.</summary>
    public required Guid DuplicateId { get; init; }

    /// <summary>
    /// What to keep for each field the two profiles disagree on. A choice applies only when the field
    /// still conflicts at merge time; every other field follows the automatic rule in
    /// <see cref="PersonMergeRules.Combine"/>. A missing choice falls back to
    /// <see cref="PersonMergeRules.DefaultChoice"/>.
    /// </summary>
    public IReadOnlyDictionary<MergeField, MergeFieldChoice>? FieldChoices { get; init; }
}
