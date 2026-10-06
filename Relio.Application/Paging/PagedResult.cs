namespace Relio.Application.Paging;

/// <summary>
/// One page of a larger, ordered list.
/// </summary>
/// <typeparam name="T">The kind of row on the page.</typeparam>
/// <param name="Items">The rows on this page, already in display order.</param>
/// <param name="Page">The 1-based page these rows are, which is the page that was actually returned: a service clamps an out-of-range request, so this may differ from the one asked for.</param>
/// <param name="PageSize">How many rows a full page holds.</param>
/// <param name="TotalCount">How many rows there are across every page.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    /// <summary>
    /// How many pages there are. Always at least 1: an empty list is still one (empty) page, so a
    /// caller never has to special-case "page 1 of 0".
    /// </summary>
    public int PageCount => TotalCount == 0 ? 1 : (TotalCount + PageSize - 1) / PageSize;
}
