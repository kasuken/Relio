using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Domain;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for <see cref="Person"/>.</summary>
public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.Property(p => p.OwnerId)
            .IsRequired()
            .HasMaxLength(450); // matches ASP.NET Core Identity's default user id column length.

        builder.Property(p => p.FirstName)
            .IsRequired()
            .HasMaxLength(Person.FirstNameMaxLength);

        builder.Property(p => p.LastName)
            .HasMaxLength(Person.LastNameMaxLength);

        builder.Property(p => p.Nickname)
            .HasMaxLength(Person.NicknameMaxLength);

        builder.Property(p => p.HowWeMet)
            .HasMaxLength(Person.HowWeMetMaxLength);

        builder.Property(p => p.Details)
            .HasMaxLength(Person.DetailsMaxLength);

        // Computed from the three Birthday* columns and DisplayName from the names: never mapped,
        // never usable in a query.
        builder.Ignore(p => p.DisplayName);
        builder.Ignore(p => p.Birthday);

        // Deleting a relationship type must never delete the people who had it - they just lose
        // the label (issue #25 reassigns them first when the user asks to). The foreign key's
        // default index is kept: it serves the "how many people use this type" lookup.
        builder.HasOne(p => p.RelationshipType)
            .WithMany()
            .HasForeignKey(p => p.RelationshipTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        // The database refuses a birthday that cannot be a date, even if a bug in a service lets
        // one through. A real calendar check (31 April) stays in Birthday.IsValid: it would need
        // the year, which is optional here. Not enforced by the InMemory provider; proven against
        // SQL Server in Relio.Data.IntegrationTests (PersonSchemaSqlServerTests).
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_People_BirthdayMonth", "[BirthdayMonth] IS NULL OR [BirthdayMonth] BETWEEN 1 AND 12");
            table.HasCheckConstraint(
                "CK_People_BirthdayDay", "[BirthdayDay] IS NULL OR [BirthdayDay] BETWEEN 1 AND 31");
            table.HasCheckConstraint(
                "CK_People_BirthdayYear", "[BirthdayYear] IS NULL OR [BirthdayYear] BETWEEN 1 AND 9999");
            table.HasCheckConstraint(
                "CK_People_BirthdayComplete",
                "([BirthdayMonth] IS NULL AND [BirthdayDay] IS NULL AND [BirthdayYear] IS NULL) " +
                "OR ([BirthdayMonth] IS NOT NULL AND [BirthdayDay] IS NOT NULL)");
        });

        // Supports the active-people list (default view) and the archived view, both scoped to
        // the owner - the two query shapes every list screen in Relio needs.
        builder.HasIndex(p => new { p.OwnerId, p.IsArchived });

        builder.HasMany(p => p.Tags)
            .WithMany(t => t.People)
            .UsingEntity(join => join.ToTable("PersonTags"));
    }
}
