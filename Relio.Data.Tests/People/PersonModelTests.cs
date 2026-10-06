using Microsoft.EntityFrameworkCore;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// The shape of the <see cref="Person"/> model the people list (issue #23) depends on. The
/// indexes themselves are proven against a real SQL Server in <c>Relio.Data.IntegrationTests</c>;
/// this guards the model so a refactor of <c>PersonConfiguration</c> cannot silently drop one.
/// </summary>
public class PersonModelTests
{
    [Fact]
    public void Person_has_an_owner_scoped_index_for_every_list_sort()
    {
        var indexes = IndexColumns();

        indexes.Should().ContainEquivalentOf(new[] { "OwnerId", "IsArchived", "FirstName", "LastName" });
        indexes.Should().ContainEquivalentOf(new[] { "OwnerId", "IsArchived", "CreatedAtUtc" });
        indexes.Should().ContainEquivalentOf(new[] { "OwnerId", "IsArchived", "LastContactedOn" });
    }

    [Fact]
    public void The_old_owner_archived_index_is_replaced()
    {
        IndexColumns().Should().NotContain(
            columns => columns.SequenceEqual(new[] { "OwnerId", "IsArchived" }),
            "the three composite indexes cover the counts and every sort, so the bare pair is redundant");
    }

    [Fact]
    public void LastContactedOn_is_a_nullable_date_column()
    {
        using var dbContext = CreateDbContext();
        var property = dbContext.Model.FindEntityType(typeof(Person))!.FindProperty(nameof(Person.LastContactedOn))!;

        property.IsNullable.Should().BeTrue();
        property.ClrType.Should().Be(typeof(DateOnly?));
        property.FindAnnotation("Relational:ColumnType")!.Value.Should().Be("date");
    }

    private static List<string[]> IndexColumns()
    {
        using var dbContext = CreateDbContext();
        return dbContext.Model.FindEntityType(typeof(Person))!
            .GetIndexes()
            .Select(i => i.Properties.Select(p => p.Name).ToArray())
            .ToList();
    }

    private static RelioDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, TimeProvider.System);
}
