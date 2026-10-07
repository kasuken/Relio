using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Domain;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for the owner-scoped links between interactions and people.</summary>
public sealed class InteractionParticipantConfiguration : IEntityTypeConfiguration<InteractionParticipant>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InteractionParticipant> builder)
    {
        builder.Property(participant => participant.OwnerId)
            .IsRequired()
            .HasMaxLength(450);

        builder.HasOne(participant => participant.Person)
            .WithMany()
            .HasForeignKey(participant => participant.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(participant => new { participant.InteractionId, participant.PersonId })
            .IsUnique();

        builder.HasIndex(participant => new
        {
            participant.OwnerId,
            participant.PersonId,
            participant.InteractionId,
        });
    }
}
