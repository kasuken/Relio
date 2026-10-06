using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Data.Identity;

namespace Relio.Data.Configurations;

/// <summary>
/// EF Core mapping for the Relio-specific columns of <see cref="RelioUser"/>. Identity's own
/// columns keep the mapping <c>IdentityDbContext</c> gives them.
/// </summary>
public sealed class RelioUserConfiguration : IEntityTypeConfiguration<RelioUser>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RelioUser> builder)
    {
        builder.Property(u => u.PendingEmail)
            .HasMaxLength(256); // same length Identity gives Email and NormalizedEmail.

        // Self-hosted administration (#19). Not indexed: it is only ever read together with the
        // user row, which sign-in and the admin account list already load by id/email.
        builder.Property(u => u.IsDisabled)
            .IsRequired();
    }
}
