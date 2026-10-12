using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Relio.Domain;

namespace Relio.Data.Tests.People;

/// <summary>
/// Guards the delete checklist on <c>PeopleService.RemoveDependentsAsync</c> (issue #26). The
/// InMemory provider only cascades to dependents it tracks and enforces no foreign keys, so a new
/// child of a person that nobody remembered to remove explicitly would be orphaned by a delete
/// and still pass every other unit test. These tests read the model instead, so adding an entity
/// that references <see cref="Person"/> fails here until the author has done the checklist.
/// </summary>
public class PersonDeleteChecklistTests
{
    /// <summary>
    /// The entities that reference a person, each handled by <c>PeopleService.RemoveDependentsAsync</c>:
    /// the tag links (the <c>PersonTags</c> join, cleared with <c>person.Tags.Clear()</c>), contact
    /// methods, notes, reminders, and interaction participant links (which preserve a shared interaction
    /// for its other participants and delete it when the last link goes).
    /// </summary>
    private static readonly string[] HandledByRemoveDependents =
    [
        "PersonTag",
        typeof(ContactMethod).FullName!,
        typeof(Note).FullName!,
        typeof(InteractionParticipant).FullName!,
        typeof(Reminder).FullName!,
        typeof(DifficultMoment).FullName!,
        typeof(PersonLink).FullName!,
    ];

    /// <summary>
    /// The one place a foreign key to <see cref="Person"/> is deliberately not a cascade: a table with two
    /// foreign keys to <c>People</c> cannot cascade both on SQL Server (multiple cascade paths), so
    /// <see cref="PersonLink.RelatedPersonId"/> is <c>NoAction</c> and
    /// <c>PeopleService.RemoveDependentsAsync</c> deletes the links where the person is the related end.
    /// </summary>
    private static readonly (Type Entity, string Property)[] NoActionDeletedExplicitly =
    [
        (typeof(PersonLink), nameof(PersonLink.RelatedPersonId)),
    ];

    [Fact]
    public void Every_relationship_to_a_person_cascades_on_delete()
    {
        var references = ForeignKeysToPerson()
            .Where(foreignKey => !IsAllowedNoAction(foreignKey))
            .ToList();

        references.Should().NotBeEmpty();
        references.Should().OnlyContain(
            foreignKey => foreignKey.DeleteBehavior == DeleteBehavior.Cascade,
            "the database cascade is the SQL Server backstop behind PeopleService.RemoveDependentsAsync; if a table "
            + "cannot cascade (two foreign keys to People), use NoAction, delete those rows in RemoveDependentsAsync, "
            + "and add the foreign key to NoActionDeletedExplicitly");
    }

    [Fact]
    public void The_no_action_allow_list_matches_the_model()
    {
        var noAction = ForeignKeysToPerson()
            .Where(foreignKey => foreignKey.DeleteBehavior != DeleteBehavior.Cascade)
            .Select(foreignKey => (foreignKey.DeclaringEntityType.ClrType, foreignKey.Properties.Single().Name))
            .ToList();

        noAction.Should().BeEquivalentTo(NoActionDeletedExplicitly);
        ForeignKeysToPerson()
            .Where(IsAllowedNoAction)
            .Should().OnlyContain(foreignKey => foreignKey.DeleteBehavior == DeleteBehavior.NoAction);
    }

    private static bool IsAllowedNoAction(IForeignKey foreignKey) =>
        NoActionDeletedExplicitly.Any(allowed =>
            allowed.Entity == foreignKey.DeclaringEntityType.ClrType
            && foreignKey.Properties.Single().Name == allowed.Property);

    [Fact]
    public void The_delete_checklist_covers_every_entity_that_references_a_person()
    {
        var referencing = ForeignKeysToPerson()
            .Select(foreignKey => foreignKey.DeclaringEntityType.Name)
            .Distinct()
            .ToList();

        referencing.Should().BeEquivalentTo(
            HandledByRemoveDependents,
            "a new child of a person needs one line in PeopleService.RemoveDependentsAsync, its entry in this list, "
            + "and a delete test. Add the new child to PeopleService.RemoveDependentsAsync and to this list.");
    }

    private static List<IForeignKey> ForeignKeysToPerson()
    {
        using var dbContext = new RelioDbContext(new DbContextOptionsBuilder<RelioDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, TimeProvider.System, FieldProtector);

        return dbContext.Model.FindEntityType(typeof(Person))!.GetReferencingForeignKeys().ToList();
    }
}
