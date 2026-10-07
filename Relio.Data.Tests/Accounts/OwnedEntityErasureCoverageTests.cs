using Microsoft.EntityFrameworkCore;
using Relio.Data.Accounts;
using Relio.Data.Tests.Administration;
using Relio.Domain;
using static Relio.Data.Tests.Administration.AdministrationTestHarness;

namespace Relio.Data.Tests.Accounts;

public sealed class OwnedEntityErasureCoverageTests
{
    [Fact]
    public void Every_mapped_owned_entity_is_in_the_account_erasure_checklist()
    {
        using var dbContext = CreateDbContext(NewDatabase());

        var mappedTypes = dbContext.Model.GetEntityTypes()
            .Where(entityType => typeof(IOwnedEntity).IsAssignableFrom(entityType.ClrType))
            .Select(entityType => entityType.ClrType);
        var deletionChecklist = new[]
        {
            typeof(Person),
            typeof(ContactMethod),
            typeof(Tag),
            typeof(RelationshipType),
            typeof(Interaction),
            typeof(InteractionParticipant),
            typeof(Note),
            typeof(Reminder),
            typeof(UserProfile),
            typeof(ProductActivity),
        };

        mappedTypes.Should().BeEquivalentTo(deletionChecklist);
    }

    [Fact]
    public void Every_mapped_owned_entity_has_a_non_cascading_identity_owner_foreign_key()
    {
        using var dbContext = CreateDbContext(NewDatabase());

        var ownedTypes = dbContext.Model.GetEntityTypes()
            .Where(entityType => typeof(IOwnedEntity).IsAssignableFrom(entityType.ClrType));

        foreach (var entityType in ownedTypes)
        {
            entityType.GetForeignKeys()
                .Should()
                .ContainSingle(foreignKey =>
                    foreignKey.PrincipalEntityType.ClrType == typeof(Relio.Data.Identity.RelioUser)
                    && foreignKey.Properties.Any(property => property.Name == nameof(IOwnedEntity.OwnerId))
                    && foreignKey.DeleteBehavior == DeleteBehavior.NoAction,
                    entityType.ClrType.Name);
        }
    }
}
