namespace Relio.Web.Components.Settings;

/// <summary>
/// What the user chose in <see cref="RemoveRelationshipTypeDialog"/>.
/// </summary>
/// <param name="ReassignToId">The type to move the people to, or <see langword="null"/> to leave them without one.</param>
public sealed record RemoveRelationshipTypeResult(Guid? ReassignToId);
