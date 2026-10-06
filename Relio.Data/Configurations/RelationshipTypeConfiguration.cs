using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Domain;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for <see cref="RelationshipType"/>.</summary>
public sealed class RelationshipTypeConfiguration : IEntityTypeConfiguration<RelationshipType>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RelationshipType> builder)
    {
        builder.Property(t => t.OwnerId)
            .IsRequired()
            .HasMaxLength(450); // matches ASP.NET Core Identity's default user id column length.

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(RelationshipType.NameMaxLength);

        // A user cannot have two relationship types with the same name (case-insensitively on
        // SQL Server's default collation); also the lookup path used to validate the id supplied
        // to IPeopleService.
        builder.HasIndex(t => new { t.OwnerId, t.Name }).IsUnique();

        // The list the user picks from is always read in this order.
        builder.HasIndex(t => new { t.OwnerId, t.SortOrder });
    }
}
