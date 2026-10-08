using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Domain;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for <see cref="DifficultMoment"/>.</summary>
public sealed class DifficultMomentConfiguration : IEntityTypeConfiguration<DifficultMoment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DifficultMoment> builder)
    {
        builder.ToTable("DifficultMoments");

        builder.Property(m => m.OwnerId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(m => m.OccurredOn)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(m => m.ResolvedOn)
            .HasColumnType("date");

        builder.Property(m => m.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.Description)
            .IsRequired()
            .HasMaxLength(Encryption.FieldProtectionSchema.MaxStoredLength(DifficultMoment.DescriptionMaxLength));

        builder.Property(m => m.Trigger)
            .HasMaxLength(Encryption.FieldProtectionSchema.MaxStoredLength(DifficultMoment.TriggerMaxLength));

        builder.Property(m => m.Resolution)
            .HasMaxLength(Encryption.FieldProtectionSchema.MaxStoredLength(DifficultMoment.ResolutionMaxLength));

        builder.Property(m => m.LessonsLearned)
            .HasMaxLength(Encryption.FieldProtectionSchema.MaxStoredLength(DifficultMoment.LessonsLearnedMaxLength));

        // A difficult moment belongs to a person. Cascades on delete in SQL Server.
        builder.HasOne(m => m.Person)
            .WithMany(p => p.DifficultMoments)
            .HasForeignKey(m => m.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // A difficult moment can optionally link to an earlier moment as a recurrence.
        // Restrict prevents multiple cascade paths or cycles on SQL Server.
        builder.HasOne(m => m.RecurrenceOf)
            .WithMany(m => m.Recurrences)
            .HasForeignKey(m => m.RecurrenceOfId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes for owner-scoped lookups, timeline streams and status filtering
        builder.HasIndex(m => new { m.OwnerId, m.PersonId, m.OccurredOn });
        builder.HasIndex(m => new { m.OwnerId, m.PersonId, m.CreatedAtUtc, m.Id });
        builder.HasIndex(m => new { m.OwnerId, m.Status, m.OccurredOn });
        builder.HasIndex(m => new { m.OwnerId, m.RecurrenceOfId });
    }
}
