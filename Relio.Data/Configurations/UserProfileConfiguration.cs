using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Domain;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for <see cref="UserProfile"/>.</summary>
public sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.Property(p => p.OwnerId)
            .IsRequired()
            .HasMaxLength(450); // matches ASP.NET Core Identity's default user id column length.

        builder.Property(p => p.TimeZoneId)
            .IsRequired()
            .HasMaxLength(100); // comfortably fits every IANA id, e.g. "America/Argentina/Buenos_Aires".

        // Exactly one profile per user.
        builder.HasIndex(p => p.OwnerId).IsUnique();
    }
}
