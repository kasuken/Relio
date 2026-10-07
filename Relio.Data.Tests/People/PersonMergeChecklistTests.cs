using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// Guards the merge checklist on <c>PersonMergeService.MoveDependentsAsync</c> (issue #28), the way
/// <see cref="PersonDeleteChecklistTests"/> guards <c>RemoveDependentsAsync</c>. They read the model, so
/// adding an entity that references <see cref="Person"/>, or a new column on <see cref="Person"/>, fails
/// here until the author has decided how merging handles it - a merge that forgets a child would
/// silently delete its rows with the duplicate.
/// </summary>
public class PersonMergeChecklistTests
{
    /// <summary>
    /// The entities that reference a person, each handled by <c>PersonMergeService.MoveDependentsAsync</c>:
    /// the tag links (the <c>PersonTags</c> join), contact methods, and interaction participant
    /// links (reassigned to the primary unless that interaction already has the primary), and notes
    /// (reassigned to the primary while retaining their text, pin state and audit dates).
    /// </summary>
    private static readonly string[] HandledByMoveDependents =
    [
        "PersonTag",
        typeof(ContactMethod).FullName!,
        typeof(Note).FullName!,
        typeof(InteractionParticipant).FullName!,
    ];

    /// <summary>
    /// Every mapped column of <see cref="Person"/> except the identity and audit columns, each with a rule in
    /// <c>PersonMergeRules.Combine</c>.
    /// </summary>
    private static readonly string[] HandledByCombine =
    [
        nameof(Person.FirstName),
        nameof(Person.LastName),
        nameof(Person.Nickname),
        nameof(Person.RelationshipTypeId),
        nameof(Person.BirthdayYear),
        nameof(Person.BirthdayMonth),
        nameof(Person.BirthdayDay),
        nameof(Person.HowWeMet),
        nameof(Person.Details),
        nameof(Person.IsArchived),
        nameof(Person.ArchivedAtUtc),
        nameof(Person.LastContactedOn),
    ];

    [Fact]
    public void The_merge_checklist_covers_every_entity_that_references_a_person()
    {
        var referencing = ForeignKeysToPerson()
            .Select(foreignKey => foreignKey.DeclaringEntityType.Name)
            .Distinct()
            .ToList();

        referencing.Should().BeEquivalentTo(
            HandledByMoveDependents,
            "A new entity references Person. Move its rows in PersonMergeService.MoveDependentsAsync, delete them in "
            + "PeopleService.RemoveDependentsAsync, and add it to both checklist tests (this one and PersonDeleteChecklistTests).");
    }

    [Fact]
    public void Every_person_column_has_a_merge_rule()
    {
        var columns = Model().FindEntityType(typeof(Person))!
            .GetProperties()
            .Select(property => property.Name)
            .Except([nameof(Person.Id), nameof(Person.OwnerId), nameof(Person.CreatedAtUtc), nameof(Person.UpdatedAtUtc)])
            .ToList();

        var undecided = columns.Except(HandledByCombine).ToList();
        undecided.Should().BeEmpty(
            "Decide how PersonMergeRules.Combine merges Person.{0}, then add it to HandledByCombine here.",
            string.Join(", Person.", undecided));
        HandledByCombine.Except(columns).Should().BeEmpty("a column listed here no longer exists on Person");
    }

    [Fact]
    public void Every_relationship_to_a_person_cascades_so_merge_can_rely_on_the_backstop()
    {
        var references = ForeignKeysToPerson();

        references.Should().NotBeEmpty();
        references.Should().OnlyContain(
            foreignKey => foreignKey.DeleteBehavior == DeleteBehavior.Cascade,
            "removing the duplicate relies on the database cascade for anything MoveDependentsAsync did not load");
    }

    private static IModel Model()
    {
        using var dbContext = new RelioDbContext(
            new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            TimeProvider.System);

        return dbContext.Model;
    }

    private static List<IForeignKey> ForeignKeysToPerson() =>
        Model().FindEntityType(typeof(Person))!.GetReferencingForeignKeys().ToList();
}
