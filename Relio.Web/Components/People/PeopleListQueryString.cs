using System.Globalization;
using Relio.Application.People;

namespace Relio.Web.Components.People;

/// <summary>
/// The contract between the people list and its address: <c>/people?sort=contacted&amp;archived=true&amp;page=2</c>.
/// The view lives in the URL, so Back restores it, a reload keeps it and it can be bookmarked.
/// Pure and symmetrical (<see cref="Parse"/> and <see cref="ToRelativeUri"/> round-trip), so it is
/// unit tested without a component.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never anything personal.</b> Only these three values ever go in the address: a sort token, the
/// word <c>true</c> and a page number. No name, no search text and no id - an address lands in
/// browser history, tab lists, proxy logs and referrers (see the gdpr-compliant skill).
/// </para>
/// <para>
/// <b>Forgiving on the way in, canonical on the way out.</b> A query string is editable by anyone, so
/// every value falls back to its default rather than failing: an unknown sort is <c>name</c>, anything
/// but <c>true</c> is "not archived", and anything but a positive whole number is page 1. Defaults are
/// left out of the address, and the parameters are always written in the same order.
/// </para>
/// </remarks>
public static class PeopleListQueryString
{
    /// <summary>The query parameter that holds the sort token.</summary>
    public const string SortParameter = "sort";

    /// <summary>The query parameter that is <c>true</c> when archived people are shown.</summary>
    public const string ArchivedParameter = "archived";

    /// <summary>The query parameter that holds the 1-based page number.</summary>
    public const string PageParameter = "page";

    /// <summary>The query parameter that holds the search query.</summary>
    public const string SearchParameter = "q";

    /// <summary>The query parameter that holds tag filter values.</summary>
    public const string TagParameter = "tag";

    /// <summary>The query parameter that holds relationship type filter values.</summary>
    public const string TypeParameter = "type";

    private const string PeopleRoute = "/people";
    private const string NameToken = "name";
    private const string RecentlyAddedToken = "added";
    private const string LastContactedToken = "contacted";
    private const string TrueToken = "true";

    /// <summary>
    /// Reads the raw query values into a query.
    /// </summary>
    public static PeopleListQuery Parse(
        string? sort,
        string? archived,
        string? page,
        string? q = null,
        IEnumerable<string>? tags = null,
        IEnumerable<string>? types = null) => new()
    {
        Sort = ParseSort(sort),
        IncludeArchived = string.Equals(archived, TrueToken, StringComparison.OrdinalIgnoreCase),
        Page = ParsePage(page),
        SearchTerm = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
        Tags = ParseFilterValues(tags),
        RelationshipTypes = ParseFilterValues(types),
    };

    /// <summary>
    /// Overload taking single string filter values (comma-separated or single value).
    /// </summary>
    public static PeopleListQuery Parse(
        string? sort,
        string? archived,
        string? page,
        string? q,
        string? tag,
        string? type) =>
        Parse(
            sort,
            archived,
            page,
            q,
            tag is null ? null : [tag],
            type is null ? null : [type]);

    /// <summary>
    /// The address of <paramref name="query"/>: <c>/people</c> for the defaults, otherwise only the
    /// parameters that differ from them, in the fixed order sort, archived, page, search, tags, types.
    /// </summary>
    public static string ToRelativeUri(PeopleListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var parameters = new List<string>(6);
        if (query.Sort != PeopleSort.Name)
        {
            parameters.Add($"{SortParameter}={ToToken(query.Sort)}");
        }

        if (query.IncludeArchived)
        {
            parameters.Add($"{ArchivedParameter}={TrueToken}");
        }

        if (query.Page > 1)
        {
            parameters.Add($"{PageParameter}={query.Page.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            parameters.Add($"{SearchParameter}={Uri.EscapeDataString(query.SearchTerm.Trim())}");
        }

        foreach (var tag in query.Tags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                parameters.Add($"{TagParameter}={Uri.EscapeDataString(tag.Trim())}");
            }
        }

        foreach (var type in query.RelationshipTypes)
        {
            if (!string.IsNullOrWhiteSpace(type))
            {
                parameters.Add($"{TypeParameter}={Uri.EscapeDataString(type.Trim())}");
            }
        }

        return parameters.Count == 0 ? PeopleRoute : $"{PeopleRoute}?{string.Join('&', parameters)}";
    }

    private static PeopleSort ParseSort(string? sort) => sort?.Trim().ToLowerInvariant() switch
    {
        RecentlyAddedToken => PeopleSort.RecentlyAdded,
        LastContactedToken => PeopleSort.LastContacted,
        _ => PeopleSort.Name,
    };

    private static string ToToken(PeopleSort sort) => sort switch
    {
        PeopleSort.RecentlyAdded => RecentlyAddedToken,
        PeopleSort.LastContacted => LastContactedToken,
        _ => NameToken,
    };

    // NumberStyles.None: digits only - no sign, no spaces, no separators. Too large for an int
    // fails the parse too, which falls back to page 1 like any other garbage.
    private static int ParsePage(string? page) =>
        int.TryParse(page, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1 ? number : 1;

    private static IReadOnlyList<string> ParseFilterValues(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var v in values)
        {
            if (string.IsNullOrWhiteSpace(v))
            {
                continue;
            }

            foreach (var part in v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(part) && !result.Contains(part, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(part);
                }
            }
        }

        return result;
    }
}
