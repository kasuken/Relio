using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Relio.Data.Identity;
using Relio.Domain;

namespace Relio.Data;

/// <summary>
/// Entity Framework Core database context for Relio. Entity configurations are discovered
/// automatically from this assembly via <see cref="ModelBuilder.ApplyConfigurationsFromAssembly"/>.
/// </summary>
/// <remarks>
/// Audit timestamps (<see cref="IOwnedEntity.CreatedAtUtc"/>, <see cref="IOwnedEntity.UpdatedAtUtc"/>)
/// are stamped here, from <paramref name="timeProvider"/>, on every <c>SaveChanges</c> call - callers
/// and application services never set them directly. <see cref="IOwnedEntity.OwnerId"/> is not
/// touched here: it is set once by the service that creates the entity. This context does not
/// apply a global query filter on owner id; see the "User-scoped data pattern" section of
/// AGENTS.md for why ownership is instead enforced explicitly in each service method.
/// </remarks>
/// <remarks>
/// A <see cref="DbContext"/> allows one operation in flight, and the scoped instance is shared by
/// every component of a Blazor circuit, so <see cref="Lane"/> serializes data service calls on it.
/// </remarks>
/// <remarks>
/// Inherits <see cref="IdentityDbContext{TUser}"/> (epic #14) instead of plain <see cref="DbContext"/>
/// so ASP.NET Core Identity's own tables (<c>AspNetUsers</c>, <c>AspNetUserClaims</c>, etc.) live in
/// the same database and migration history as the rest of Relio. Identity's own entities are not
/// <see cref="IOwnedEntity"/> - they are not user-owned data, they *are* the user - so they are
/// untouched by <see cref="ApplyAuditTimestamps"/> below.
/// </remarks>
public sealed class RelioDbContext(DbContextOptions<RelioDbContext> options, TimeProvider timeProvider)
    : IdentityDbContext<RelioUser>(options)
{
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <summary>
    /// Lets one operation at a time run on this context. A Blazor circuit (and a prerender) shares one
    /// scoped context across components, and the renderer starts a sibling's load while the previous
    /// one is still awaiting the database - see "One database operation at a time" in AGENTS.md.
    /// Every data service runs through it (<c>AddRelioData</c> wraps them). It is idle whenever no
    /// data service call is running, so it stays correct if context pooling is adopted later. Not
    /// mapped: EF Core only maps <see cref="DbSet{TEntity}"/> properties, so there is no model change.
    /// </summary>
    public Concurrency.DatabaseLane Lane { get; } = new();

    /// <summary>The current user's people (filtered explicitly by services, not by a global query filter).</summary>
    public DbSet<Person> People => Set<Person>();

    /// <summary>The current user's tags.</summary>
    public DbSet<Tag> Tags => Set<Tag>();

    /// <summary>The current user's people's contact methods (email, phone, ...), filtered explicitly by services like everything else.</summary>
    public DbSet<ContactMethod> ContactMethods => Set<ContactMethod>();

    /// <summary>The current user's relationship types ("Friend", "Colleague", ...), seeded per user at registration.</summary>
    public DbSet<RelationshipType> RelationshipTypes => Set<RelationshipType>();

    /// <summary>User profiles (currently just the user's time zone, see epic #12), one per user.</summary>
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    /// <summary>
    /// Pending sign-up invitations for an invitation-only instance (issue #19). Instance
    /// administration data, not user-owned: see <see cref="Administration.RegistrationInvitation"/>.
    /// </summary>
    public DbSet<Administration.RegistrationInvitation> RegistrationInvitations => Set<Administration.RegistrationInvitation>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RelioDbContext).Assembly);
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyAuditTimestamps()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var entry in ChangeTracker.Entries<IOwnedEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.UpdatedAtUtc = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = now;
                    break;
            }
        }
    }
}
