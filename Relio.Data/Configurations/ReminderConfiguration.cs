using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Domain;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for <see cref="Reminder"/>.</summary>
public sealed class ReminderConfiguration : IEntityTypeConfiguration<Reminder>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Reminder> builder)
    {
        builder.Property(r => r.OwnerId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(r => r.Title)
            .IsRequired()
            .HasMaxLength(Reminder.TitleMaxLength);

        builder.Property(r => r.DueDate)
            .HasColumnType("date");

        builder.Property(r => r.SnoozedUntilDate)
            .HasColumnType("date");

        builder.Property(r => r.LastDeliveredDate)
            .HasColumnType("date");

        builder.Property(r => r.Frequency)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // Computed and ignored by EF Core:
        builder.Ignore(r => r.EffectiveDueDate);

        // A reminder belongs to a person. Cascades on delete in SQL Server.
        builder.HasOne(r => r.Person)
            .WithMany(p => p.Reminders)
            .HasForeignKey(r => r.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes:
        // (OwnerId, PersonId): reading reminders per person
        builder.HasIndex(r => new { r.OwnerId, r.PersonId });

        // (OwnerId, IsCompleted, DueDate): querying due/open reminders in user dashboard and reminders list
        builder.HasIndex(r => new { r.OwnerId, r.IsCompleted, r.DueDate });
    }
}
