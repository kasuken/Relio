using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Domain;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for the minimal product activity contribution (issue #62).</summary>
public sealed class ProductActivityConfiguration : IEntityTypeConfiguration<ProductActivity>
{
    /// <summary>The one-row-per-owner unique index used to identify a create race safely.</summary>
    public const string OwnerIndexName = "IX_ProductActivities_OwnerId";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProductActivity> builder)
    {
        builder.ToTable("ProductActivities");

        builder.Property(activity => activity.OwnerId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(activity => activity.CohortStartedOnUtc)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(activity => activity.LastActiveOnUtc)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(activity => activity.ReturnedInDays30To59)
            .IsRequired();

        builder.Property(activity => activity.RetentionExpiresAtUtc)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.HasIndex(activity => activity.OwnerId)
            .IsUnique();

        builder.HasIndex(activity => activity.CohortStartedOnUtc);
        builder.HasIndex(activity => activity.RetentionExpiresAtUtc);
    }
}
