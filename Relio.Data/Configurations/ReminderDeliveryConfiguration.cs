using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Data.Identity;
using Relio.Data.Reminders;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for <see cref="ReminderDelivery"/>.</summary>
public sealed class ReminderDeliveryConfiguration : IEntityTypeConfiguration<ReminderDelivery>
{
    /// <summary>The unique index a competing worker's claim loses its save on.</summary>
    public const string DeliveryKeyIndexName = "IX_ReminderDeliveries_DeliveryKey";

    /// <summary>The index the retention purge walks.</summary>
    public const string ClaimedAtIndexName = "IX_ReminderDeliveries_ClaimedAtUtc";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ReminderDelivery> builder)
    {
        builder.ToTable("ReminderDeliveries", table =>
        {
            table.HasCheckConstraint("CK_ReminderDeliveries_Kind", "[Kind] IN (N'Reminder', N'Birthday')");
            table.HasCheckConstraint("CK_ReminderDeliveries_Status", "[Status] IN (N'Pending', N'Sent')");
        });
        builder.HasKey(d => d.Id);

        builder.Property(d => d.UserId)
            .IsRequired()
            .HasMaxLength(450); // matches ASP.NET Core Identity's default user id column length.

        builder.Property(d => d.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(d => d.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(d => d.OccurrenceDate).HasColumnType("date");
        builder.Property(d => d.DeliveryDate).HasColumnType("date");

        // NO ACTION, like every owner reference: account erasure deletes the rows explicitly, and a
        // late claim for an account that no longer exists is rejected instead of leaving an orphan.
        builder.HasOne<RelioUser>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ReminderDeliveries_AspNetUsers_UserId");

        // A competing worker's claim loses its save on this index. The key is under 1,000 bytes
        // (nvarchar(450) user id, nvarchar(20) kind, guid, date), below SQL Server's 1,700-byte limit.
        builder.HasIndex(d => new { d.UserId, d.Kind, d.SourceId, d.OccurrenceDate })
            .IsUnique()
            .HasDatabaseName(DeliveryKeyIndexName);

        builder.HasIndex(d => d.ClaimedAtUtc).HasDatabaseName(ClaimedAtIndexName);
    }
}
