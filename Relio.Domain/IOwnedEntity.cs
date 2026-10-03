namespace Relio.Domain;

/// <summary>
/// Marks an entity as owned by a single Relio user. Every user-owned entity in the system
/// implements this interface so services and the data layer can apply ownership checks and
/// audit timestamps uniformly. See the "User-scoped data pattern" section of AGENTS.md.
/// </summary>
public interface IOwnedEntity
{
    /// <summary>The entity's unique identifier.</summary>
    Guid Id { get; }

    /// <summary>
    /// The id of the user who owns this entity. Matches the future ASP.NET Core Identity user
    /// id (epic #14). There is no shared or team data in the MVP: every row belongs to exactly
    /// one user.
    /// </summary>
    string OwnerId { get; set; }

    /// <summary>UTC timestamp recorded when the entity was first saved.</summary>
    DateTime CreatedAtUtc { get; set; }

    /// <summary>UTC timestamp recorded the last time the entity was saved.</summary>
    DateTime UpdatedAtUtc { get; set; }
}
