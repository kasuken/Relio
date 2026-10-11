using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Data.Billing;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for <see cref="ProcessedBillingEvent"/>.</summary>
public sealed class ProcessedBillingEventConfiguration : IEntityTypeConfiguration<ProcessedBillingEvent>
{
    /// <summary>The unique index that makes a concurrent duplicate delivery lose its save.</summary>
    public const string ProviderEventIdIndexName = "IX_ProcessedBillingEvents_ProviderEventId";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProcessedBillingEvent> builder)
    {
        builder.ToTable("ProcessedBillingEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ProviderEventId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.EventType)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(e => e.ProviderEventId).IsUnique();
    }
}
