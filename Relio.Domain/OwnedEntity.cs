namespace Relio.Domain;

/// <summary>
/// Base class for every user-owned entity. Audit timestamps (<see cref="CreatedAtUtc"/>,
/// <see cref="UpdatedAtUtc"/>) are UTC instants and are populated by the data layer (see
/// <c>Relio.Data.RelioDbContext.SaveChangesAsync</c>), not by callers. <see cref="OwnerId"/> is
/// set once, by the application service that creates the entity, from
/// <c>Relio.Application.Security.ICurrentUser</c> - never from caller-supplied input.
/// </summary>
public abstract class OwnedEntity : IOwnedEntity
{
    /// <inheritdoc />
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <inheritdoc />
    public string OwnerId { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTime CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public DateTime UpdatedAtUtc { get; set; }
}
