using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Domain;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for <see cref="Tag"/>.</summary>
public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.Property(t => t.OwnerId)
            .IsRequired()
            .HasMaxLength(450); // matches ASP.NET Core Identity's default user id column length.

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(50);

        // A user cannot have two tags with the same name; also the lookup path used to
        // validate the tag ids supplied to IPeopleService.
        builder.HasIndex(t => new { t.OwnerId, t.Name }).IsUnique();
    }
}
