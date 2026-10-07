namespace Relio.Application.Dashboard;

/// <summary>The minimal active-person details shown in the recently added section.</summary>
/// <param name="PersonId">The person's opaque identifier.</param>
/// <param name="DisplayName">The person's display name.</param>
/// <param name="AddedOn">The user's calendar date when the person was added.</param>
public sealed record DashboardPersonItem(Guid PersonId, string DisplayName, DateOnly AddedOn);
