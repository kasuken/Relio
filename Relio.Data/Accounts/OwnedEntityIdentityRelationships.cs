using Microsoft.EntityFrameworkCore;
using Relio.Data.Identity;
using Relio.Domain;

namespace Relio.Data.Accounts;

/// <summary>
/// Adds a non-cascading owner foreign key from every <see cref="IOwnedEntity"/> to the Identity
/// account that owns it.
/// </summary>
/// <remarks>
/// Call from <c>RelioDbContext.OnModelCreating</c> after applying entity configurations. The
/// NO ACTION relationship is the SQL Server backstop for a stale circuit racing account erasure:
/// any late owner row prevents the Identity row from being deleted, so the single SaveChanges
/// transaction rolls back in full. Adding the relationship to a database with orphan owner ids
/// intentionally fails the migration; it never adopts or deletes those rows.
/// </remarks>
public static class OwnedEntityIdentityRelationships
{
    /// <summary>Applies the required owner foreign key to each mapped user-owned entity.</summary>
    /// <param name="modelBuilder">The EF Core model builder.</param>
    public static void Apply(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var ownedEntityTypes = modelBuilder.Model.GetEntityTypes()
            .Where(entityType => typeof(IOwnedEntity).IsAssignableFrom(entityType.ClrType))
            .ToArray();

        foreach (var entityType in ownedEntityTypes)
        {
            var tableName = entityType.GetTableName()
                ?? throw new InvalidOperationException(
                    $"Owned entity '{entityType.ClrType.Name}' must map to a table.");

            modelBuilder.Entity(entityType.ClrType)
                .HasOne(typeof(RelioUser), navigationName: null)
                .WithMany()
                .HasForeignKey(nameof(IOwnedEntity.OwnerId))
                .IsRequired()
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName($"FK_{tableName}_AspNetUsers_OwnerId");
        }
    }
}
