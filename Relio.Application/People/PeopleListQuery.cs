namespace Relio.Application.People;

/// <summary>
/// What the people list asks for: how to order it, whether archived people are included, and
/// which page. Every property has a default, so <c>new PeopleListQuery()</c> is the default list:
/// active people by name, first page.
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
}
