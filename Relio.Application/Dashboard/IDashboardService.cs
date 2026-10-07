namespace Relio.Application.Dashboard;

/// <summary>
/// Reads the current user's dashboard overview. Each section is a small, ordered projection rather
/// than a full list of the user's records.
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Gets the current user's dashboard snapshot. Each visible list is limited to the five most
    /// relevant items, and archived people are omitted from the sections.
    /// </summary>
    /// <param name="cancellationToken">Cancels the database reads.</param>
    /// <returns>A snapshot containing user-calendar dates and owner-scoped dashboard sections.</returns>
    Task<DashboardSnapshot> GetAsync(CancellationToken cancellationToken = default);
}
