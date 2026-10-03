namespace Relio.Application.People;

/// <summary>Input for <c>IPeopleService.CreateAsync</c>.</summary>
/// <param name="FirstName">The person's first name. Required.</param>
/// <param name="LastName">The person's last name. Optional.</param>
/// <param name="Birthday">The person's birthday as a calendar date, or null if unknown.</param>
/// <param name="TagIds">
/// Ids of existing tags to attach. Every id must belong to the current user; otherwise the
/// service throws <see cref="Relio.Application.Ownership.ForeignEntityNotOwnedException"/>.
/// </param>
public sealed record CreatePersonRequest(
    string FirstName,
    string? LastName,
    DateOnly? Birthday,
    IReadOnlyCollection<Guid>? TagIds = null);
