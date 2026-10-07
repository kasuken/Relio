using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Domain;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for a shared, dated interaction.</summary>
public sealed class InteractionConfiguration : IEntityTypeConfiguration<Interaction>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Interaction> builder)
    {
        builder.Property(interaction => interaction.OwnerId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(interaction => interaction.OccurredOn)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(interaction => interaction.Kind)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(interaction => interaction.Description)
            .IsRequired()
            .HasMaxLength(Encryption.FieldProtectionSchema.MaxStoredLength(Interaction.DescriptionMaxLength));

        builder.HasMany(interaction => interaction.Participants)
            .WithOne(participant => participant.Interaction)
            .HasForeignKey(participant => participant.InteractionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(interaction => new
        {
            interaction.OwnerId,
            interaction.OccurredOn,
            interaction.CreatedAtUtc,
            interaction.Id,
        });
        builder.HasIndex(interaction => new
        {
            interaction.OwnerId,
            interaction.OccurredOn,
            interaction.Id,
        });

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_Interactions_Kind",
            "[Kind] IN (N'Call', N'Meeting', N'Message', N'Event', N'Other')"));
    }
}
