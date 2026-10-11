using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Data.Billing;
using Relio.Data.Identity;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for <see cref="UserSubscription"/>.</summary>
public sealed class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.ToTable("UserSubscriptions", table => table.HasCheckConstraint(
            "CK_UserSubscriptions_Tier", "[Tier] IN (N'Free', N'Pro')"));
        builder.HasKey(s => s.Id);

        builder.Property(s => s.UserId)
            .IsRequired()
            .HasMaxLength(450); // matches ASP.NET Core Identity's default user id column length.

        builder.Property(s => s.Tier)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(s => s.BillingProviderCustomerId).HasMaxLength(200);
        builder.Property(s => s.BillingProviderSubscriptionId).HasMaxLength(200);

        // NO ACTION, like every owner reference: account erasure deletes the row explicitly, and a
        // late write for an account that no longer exists is rejected instead of leaving an orphan.
        builder.HasOne<RelioUser>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(s => s.UserId).IsUnique();

        // Webhooks that carry no Relio metadata are matched by these.
        builder.HasIndex(s => s.BillingProviderCustomerId);
        builder.HasIndex(s => s.BillingProviderSubscriptionId);
    }
}
