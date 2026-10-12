using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Domain;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for <see cref="PersonLink"/>.</summary>
public sealed class PersonLinkConfiguration : IEntityTypeConfiguration<PersonLink>
{
    /// <summary>
    /// The unique index on one pair of people, under EF Core's default name. A race for the same pair
    /// is recognised by it (<c>SqlServerErrors.IsUniqueIndexViolation</c>); do not rename it with
    /// <c>HasDatabaseName</c>.
    /// </summary>
    public const string PairIndexName = "IX_PersonLinks_OwnerId_PersonId_RelatedPersonId";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PersonLink> builder)
    {
        builder.Property(l => l.OwnerId)
            .IsRequired()
            .HasMaxLength(450); // matches ASP.NET Core Identity's default user id column length.

        // Stored by name, with a check constraint as the database's own backstop (not enforced by
        // the InMemory provider; proven in PersonLinkSqlServerTests). Adding or renaming a kind
        // needs a migration that rewrites the constraint; PersonLinkTests pins the list.
        builder.Property(l => l.Kind)
            .HasConversion<string>()
            .HasMaxLength(PersonLink.KindMaxLength)
            .IsRequired();

        // No navigations: merge rewrites both foreign keys (see PersonLink's remarks).
        //
        // PersonId cascades like every other child. RelatedPersonId cannot: SQL Server refuses two
        // cascade paths from People to one table. So it is NO ACTION, and the services delete the
        // links where the person is the related end explicitly (PeopleService.RemoveDependentsAsync,
        // AccountDeletionService); a link created at that end between a delete's load and its save
        // makes the save fail and roll back, which is the safe direction.
        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(l => l.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(l => l.RelatedPersonId)
            .OnDelete(DeleteBehavior.NoAction);

        // One row per pair: the ends are normalized in .NET (PersonLink.SetEnds), so this index
        // forbids a pair twice in either order. (OwnerId, RelatedPersonId) serves a profile that is
        // the related end; (OwnerId, PersonId) is the prefix of the unique index.
        builder.HasIndex(l => new { l.OwnerId, l.PersonId, l.RelatedPersonId }).IsUnique();
        builder.HasIndex(l => new { l.OwnerId, l.RelatedPersonId });

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_PersonLinks_DistinctPeople", "[PersonId] <> [RelatedPersonId]");
            table.HasCheckConstraint(
                "CK_PersonLinks_Kind",
                "[Kind] IN (N'Partner', N'FormerPartner', N'Parent', N'Child', N'Sibling', N'Grandparent', "
                + "N'Grandchild', N'Relative', N'Friend', N'Colleague', N'Manager', N'DirectReport', "
                + "N'Neighbour', N'Other')");
        });
    }
}
