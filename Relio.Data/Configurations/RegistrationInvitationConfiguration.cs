using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Data.Administration;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for <see cref="RegistrationInvitation"/> (issue #19).</summary>
public sealed class RegistrationInvitationConfiguration : IEntityTypeConfiguration<RegistrationInvitation>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RegistrationInvitation> builder)
    {
        builder.ToTable("RegistrationInvitations");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Email)
            .IsRequired()
            .HasMaxLength(256); // same length Identity gives Email.

        builder.Property(i => i.NormalizedEmail)
            .IsRequired()
            .HasMaxLength(256);

        // Upper-case hex SHA-256: always exactly 64 characters.
        builder.Property(i => i.TokenHash)
            .IsRequired()
            .HasMaxLength(64)
            .IsFixedLength();

        builder.Property(i => i.CreatedByUserId)
            .IsRequired()
            .HasMaxLength(450); // matches ASP.NET Core Identity's default user id column length.

        // The lookup path when someone opens an invitation link, and the guarantee that no two
        // invitations can ever share a token.
        builder.HasIndex(i => i.TokenHash).IsUnique();

        // "Is there already an invitation for this address?" - replaced when a newer one is created.
        builder.HasIndex(i => i.NormalizedEmail);

        // Purging expired invitations and listing pending ones by expiry.
        builder.HasIndex(i => i.ExpiresAtUtc);
    }
}
