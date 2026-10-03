using Microsoft.EntityFrameworkCore;
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
public sealed class RelioDbContext(DbContextOptions<RelioDbContext> options, TimeProvider timeProvider) : DbContext(options)
{
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <summary>The current user's people (filtered explicitly by services, not by a global query filter).</summary>
    public DbSet<Person> People => Set<Person>();

    /// <summary>The current user's tags.</summary>
    public DbSet<Tag> Tags => Set<Tag>();

    /// <summary>User profiles (currently just the user's time zone, see epic #12), one per user.</summary>
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

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
