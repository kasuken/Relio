using Microsoft.EntityFrameworkCore;
using Relio.Data.Configurations;
using Relio.Data.Identity;
using Relio.Data.Reminders;
using Relio.Domain;
using static Relio.Data.Tests.Reminders.ReminderSchedulerTestData;

namespace Relio.Data.Tests.Reminders;

public class ReminderDeliveryModelTests
{
    [Fact]
    public void The_delivery_key_is_a_unique_index_over_user_kind_source_and_occurrence()
    {
        using var dbContext = CreateDbContext(NewDatabase(), TimeProvider.System);
        var entityType = dbContext.Model.FindEntityType(typeof(ReminderDelivery))!;

        var index = entityType.GetIndexes().Should().ContainSingle(i =>
            i.GetDatabaseName() == ReminderDeliveryConfiguration.DeliveryKeyIndexName).Subject;

        index.IsUnique.Should().BeTrue();
        index.Properties.Select(p => p.Name).Should().Equal(
            nameof(ReminderDelivery.UserId),
            nameof(ReminderDelivery.Kind),
            nameof(ReminderDelivery.SourceId),
            nameof(ReminderDelivery.OccurrenceDate));
    }

    [Fact]
    public void The_only_foreign_key_is_a_non_cascading_one_to_the_user()
    {
        using var dbContext = CreateDbContext(NewDatabase(), TimeProvider.System);
        var entityType = dbContext.Model.FindEntityType(typeof(ReminderDelivery))!;

        var foreignKey = entityType.GetForeignKeys().Should().ContainSingle().Subject;

        foreignKey.PrincipalEntityType.ClrType.Should().Be(typeof(RelioUser));
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.NoAction);
        foreignKey.Properties.Select(p => p.Name).Should().Equal(nameof(ReminderDelivery.UserId));
    }

    [Fact]
    public void The_ledger_is_deliberately_not_an_owned_entity()
    {
        // The owned-entity coverage tests only enumerate IOwnedEntity, so this infrastructure table
        // needs its own explicit erasure step and test (see the account deletion tests).
        typeof(IOwnedEntity).IsAssignableFrom(typeof(ReminderDelivery)).Should().BeFalse();
    }
}
