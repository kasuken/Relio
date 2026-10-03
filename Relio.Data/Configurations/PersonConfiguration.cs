using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Domain;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for <see cref="Person"/>.</summary>
public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.Property(p => p.OwnerId)
            .IsRequired()
            .HasMaxLength(450); // matches ASP.NET Core Identity's default user id column length.

        builder.Property(p => p.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.LastName)
            .HasMaxLength(100);

        builder.Ignore(p => p.DisplayName);

        // Supports the active-people list (default view) and the archived view, both scoped to
        // the owner - the two query shapes every list screen in Relio needs.
        builder.HasIndex(p => new { p.OwnerId, p.IsArchived });

        builder.HasMany(p => p.Tags)
            .WithMany(t => t.People)
            .UsingEntity(join => join.ToTable("PersonTags"));
    }
}
