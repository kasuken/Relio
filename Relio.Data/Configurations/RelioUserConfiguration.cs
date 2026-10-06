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
    }
}
