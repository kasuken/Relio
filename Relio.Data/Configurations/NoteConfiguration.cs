using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Domain;

namespace Relio.Data.Configurations;

/// <summary>Maps notes to the SQL Server schema.</summary>
public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("Notes");

        builder.Property(note => note.OwnerId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(note => note.Text)
            .IsRequired()
            .HasMaxLength(Encryption.FieldProtectionSchema.MaxStoredLength(Note.TextMaxLength));

        builder.HasOne(note => note.Person)
            .WithMany()
            .HasForeignKey(note => note.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(note => new { note.OwnerId, note.PersonId, note.CreatedAtUtc, note.Id });
        builder.HasIndex(note => new { note.OwnerId, note.PersonId, note.IsPinned });
    }
}
