using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Relio.Domain;

namespace Relio.Data.Configurations;

/// <summary>EF Core mapping for <see cref="Tag"/>.</summary>
public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    /// <summary>
    /// The name EF Core gives the unique <c>(OwnerId, Name)</c> index by convention. Services
    /// recognise a duplicate-key error by it (<c>SqlServerErrors.IsUniqueIndexViolation</c>); do not
    /// add <c>HasDatabaseName</c> - <c>DatabaseSchemaSqlServerTests</c> checks the index really has
    /// this name.
    /// </summary>
    public const string NameIndexName = "IX_Tags_OwnerId_Name";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.Property(t => t.OwnerId)
            .IsRequired()
            .HasMaxLength(450); // matches ASP.NET Core Identity's default user id column length.

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(Tag.NameMaxLength);

        // A user cannot have two tags with the same name; also the lookup path used to
        // validate the tag ids supplied to IPeopleService. SQL Server's default collation is
        // case-insensitive, so "Chess" and "chess" collide here; the InMemory provider is
        // case-sensitive, so PeopleService matches a typed name against the user's tags in code
        // first (TagNameRules.Comparer) and treats this index as the authority for a race.
        builder.HasIndex(t => new { t.OwnerId, t.Name }).IsUnique();
    }
}
