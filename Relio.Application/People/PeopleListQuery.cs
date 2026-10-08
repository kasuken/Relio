namespace Relio.Application.People;

/// <summary>
/// What the people list asks for: how to order it, whether archived people are included, search
/// text, tag and relationship type filters, and which page. Every property has a default, so
/// <c>new PeopleListQuery()</c> is the default list: active people by name, first page.
/// </summary>
public sealed record PeopleListQuery
{
    /// <summary>How many people a page holds when the caller does not say.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>The most people a page may hold. A larger <see cref="PageSize"/> is reduced to this.</summary>
    public const int MaxPageSize = 100;

    /// <summary>How the list is ordered. Must be a defined <see cref="PeopleSort"/>.</summary>
    public PeopleSort Sort { get; init; } = PeopleSort.Name;

    /// <summary>
    /// Whether archived people are listed too (mixed in, flagged by <see cref="PersonListItem.IsArchived"/>).
    /// <see langword="false"/> lists active people only, as every other view does by default.
    /// </summary>
    public bool IncludeArchived { get; init; }

    /// <summary>The 1-based page. Out of range is not an error: the service returns the nearest page that exists.</summary>
    public int Page { get; init; } = 1;

    /// <summary>How many people a page holds, from 1 to <see cref="MaxPageSize"/>; anything else is clamped into that range.</summary>
    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Optional search term to match against first name, last name, or nickname.</summary>
    public string? SearchTerm { get; init; }

    /// <summary>Tag names or IDs to filter by. People must have at least one of these tags.</summary>
    public IReadOnlyCollection<string> Tags { get; init; } = [];

    /// <summary>Relationship type names or IDs to filter by. People must have one of these relationship types.</summary>
    public IReadOnlyCollection<string> RelationshipTypes { get; init; } = [];

    /// <summary>Whether this query has any active search or filter criteria.</summary>
    public bool HasFilters => !string.IsNullOrWhiteSpace(SearchTerm) || Tags.Count > 0 || RelationshipTypes.Count > 0;

    /// <inheritdoc />
    public bool Equals(PeopleListQuery? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Sort == other.Sort
            && IncludeArchived == other.IncludeArchived
            && Page == other.Page
            && PageSize == other.PageSize
            && string.Equals(SearchTerm, other.SearchTerm, StringComparison.Ordinal)
            && Tags.SequenceEqual(other.Tags)
            && RelationshipTypes.SequenceEqual(other.RelationshipTypes);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Sort);
        hash.Add(IncludeArchived);
        hash.Add(Page);
        hash.Add(PageSize);
        hash.Add(SearchTerm);
        foreach (var tag in Tags) hash.Add(tag);
        foreach (var type in RelationshipTypes) hash.Add(type);
        return hash.ToHashCode();
    }
}
