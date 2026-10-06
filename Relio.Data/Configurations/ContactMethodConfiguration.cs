using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Domain;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for <see cref="ContactMethod"/>.</summary>
public sealed class ContactMethodConfiguration : IEntityTypeConfiguration<ContactMethod>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ContactMethod> builder)
    {
        builder.Property(c => c.OwnerId)
            .IsRequired()
            .HasMaxLength(450); // matches ASP.NET Core Identity's default user id column length.

        // Stored by name (readable, survives re-ordering the enum), with a check constraint as the
        // database's own backstop. Not enforced by the InMemory provider; proven against SQL Server
        // in Relio.Data.IntegrationTests (ContactMethodSqlServerTests). Adding or renaming a kind
        // needs a migration that rewrites the constraint.
        builder.Property(c => c.Kind)
            .HasConversion<string>()
            .HasMaxLength(ContactMethod.KindMaxLength)
            .IsRequired();

        builder.Property(c => c.Label)
            .HasMaxLength(ContactMethod.LabelMaxLength);

        builder.Property(c => c.Value)
            .IsRequired()
            .HasMaxLength(ContactMethod.ValueMaxLength);

        builder.Property(c => c.NormalizedValue)
            .IsRequired()
            .HasMaxLength(ContactMethod.NormalizedValueMaxLength);

        // A person's contact methods go with the person. The InMemory provider only cascades
        // tracked dependents, so the service removes them explicitly where it deletes a person
        // (issue #26); this is the SQL Server backstop. The foreign key's default index
        // (PersonId) is kept.
        builder.HasOne<Person>()
            .WithMany(p => p.ContactMethods)
            .HasForeignKey(c => c.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // (OwnerId, PersonId): a person's contact methods, always read per owner.
        // (OwnerId, NormalizedValue): the lookup issue #27 (duplicate detection) and issue #28
        // (merge) match on. The key is 450 chars of owner + 300 chars of value, nvarchar: 1,500
        // bytes, under SQL Server's 1,700-byte limit - which is why Value is capped at 300.
        builder.HasIndex(c => new { c.OwnerId, c.PersonId });
        builder.HasIndex(c => new { c.OwnerId, c.NormalizedValue });

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ContactMethods_Kind",
            "[Kind] IN (N'Email', N'Phone', N'Address', N'Social', N'Other')"));
    }
}
