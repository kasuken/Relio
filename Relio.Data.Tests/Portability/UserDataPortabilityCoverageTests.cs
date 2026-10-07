using Microsoft.EntityFrameworkCore;
using Relio.Application.Portability;
using Relio.Data.Encryption;
using Relio.Domain;

namespace Relio.Data.Tests.Portability;

public sealed class UserDataPortabilityCoverageTests
{
    private static readonly IReadOnlyDictionary<Type, IReadOnlyDictionary<string, string>> Classifications =
        new Dictionary<Type, IReadOnlyDictionary<string, string>>
        {
            [typeof(Person)] = Classify(
                ["Id", "CreatedAtUtc", "UpdatedAtUtc", "FirstName", "LastName", "Nickname", "RelationshipTypeId",
                    "BirthdayYear", "BirthdayMonth", "BirthdayDay", "HowWeMet", "Details", "LastContactedOn",
                    "IsArchived", "ArchivedAtUtc", "StayInTouchCadenceDays", "BirthdayReminderDisabled",
                    "BirthdayReminderLeadDays"],
                ["OwnerId"],
                [FieldProtectionSchema.VersionPropertyName]),
            [typeof(ContactMethod)] = Classify(
                ["Id", "CreatedAtUtc", "UpdatedAtUtc", "Kind", "Label", "Value", "SortOrder"],
                ["OwnerId"],
                graph: ["PersonId"],
                recomputed: ["NormalizedValue"]),
            [typeof(Tag)] = Classify(
                ["Id", "CreatedAtUtc", "UpdatedAtUtc", "Name"],
                ["OwnerId"]),
            [typeof(RelationshipType)] = Classify(
                ["Id", "CreatedAtUtc", "UpdatedAtUtc", "Name", "SortOrder"],
                ["OwnerId"]),
            [typeof(Interaction)] = Classify(
                ["Id", "CreatedAtUtc", "UpdatedAtUtc", "OccurredOn", "Kind", "Description"],
                ["OwnerId"],
                [FieldProtectionSchema.VersionPropertyName]),
            [typeof(InteractionParticipant)] = Classify(
                ["Id", "CreatedAtUtc", "UpdatedAtUtc", "PersonId"],
                ["OwnerId"],
                graph: ["InteractionId"]),
            [typeof(Note)] = Classify(
                ["Id", "CreatedAtUtc", "UpdatedAtUtc", "PersonId", "Text", "IsPinned"],
                ["OwnerId"],
                [FieldProtectionSchema.VersionPropertyName]),
            [typeof(Reminder)] = Classify(
                ["Id", "CreatedAtUtc", "UpdatedAtUtc", "PersonId", "Title", "DueDate", "Frequency",
                    "CustomIntervalMonths", "SnoozedUntilDate", "IsCompleted", "CompletedAtUtc", "LastDeliveredDate"],
                ["OwnerId"],
                [FieldProtectionSchema.VersionPropertyName]),
            [typeof(UserProfile)] = Classify(
                ["Id", "CreatedAtUtc", "UpdatedAtUtc", "TimeZoneId", "DisplayName", "OnboardingDismissed",
                    "BirthdayRemindersEnabled", "DefaultBirthdayLeadDays", "ReminderEmailDelivery"],
                ["OwnerId"],
                [FieldProtectionSchema.VersionPropertyName],
                secrets: ["UnsubscribeToken", "UnsubscribeTokenVerifier"]),
            [typeof(ProductActivity)] = Classify(
                [],
                ["OwnerId"],
                [],
                exportOnly:
                [
                    "Id", "CreatedAtUtc", "UpdatedAtUtc", "CohortStartedOnUtc", "LastActiveOnUtc",
                    "ReturnedInDays30To59", "RetentionExpiresAtUtc",
                ]),
        };

    private static readonly IReadOnlyDictionary<Type, Type> SnapshotTypes = new Dictionary<Type, Type>
    {
        [typeof(Person)] = typeof(PersonSnapshot),
        [typeof(ContactMethod)] = typeof(ContactMethodSnapshot),
        [typeof(Tag)] = typeof(TagSnapshot),
        [typeof(RelationshipType)] = typeof(RelationshipTypeSnapshot),
        [typeof(Interaction)] = typeof(InteractionSnapshot),
        [typeof(InteractionParticipant)] = typeof(InteractionParticipantSnapshot),
        [typeof(Note)] = typeof(NoteSnapshot),
        [typeof(Reminder)] = typeof(ReminderSnapshot),
        [typeof(UserProfile)] = typeof(UserProfileSnapshot),
        [typeof(ProductActivity)] = typeof(ProductActivitySnapshot),
    };

    [Fact]
    public void Every_owned_model_property_has_an_explicit_portability_classification()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var dbContext = new RelioDbContext(options, TimeProvider.System, FieldProtector);

        var entities = dbContext.Model.GetEntityTypes()
            .Where(entity => typeof(IOwnedEntity).IsAssignableFrom(entity.ClrType))
            .ToDictionary(entity => entity.ClrType, entity => entity.GetProperties().Select(property => property.Name).ToHashSet());

        entities.Keys.Should().BeEquivalentTo(Classifications.Keys,
            "adding an owned record type must make its export, exclusion, and restore behavior explicit");

        foreach (var (entityType, actualProperties) in entities)
        {
            var classifications = Classifications[entityType];
            actualProperties.Should().BeEquivalentTo(classifications.Keys,
                $"every persisted {entityType.Name} property must be exported, recomputed, private, or export-only");

            var snapshotType = SnapshotTypes[entityType];
            foreach (var (propertyName, classification) in classifications)
            {
                if (classification is "json" or "export-only")
                {
                    snapshotType.GetProperty(propertyName).Should().NotBeNull(
                        $"{entityType.Name}.{propertyName} is part of the declared export format");
                }

                if (classification is "private" or "secret" or "derived" or "graph" or "recomputed" or "security-metadata")
                {
                    snapshotType.GetProperty(propertyName).Should().BeNull(
                        $"{entityType.Name}.{propertyName} must not be serialized as an ordinary field");
                }
            }
        }
    }

    [Fact]
    public void Computed_domain_properties_and_sensitive_fields_are_named_as_explicit_exclusions()
    {
        typeof(Person).GetProperty(nameof(Person.DisplayName)).Should().NotBeNull();
        typeof(Person).GetProperty(nameof(Person.Birthday)).Should().NotBeNull();
        typeof(Reminder).GetProperty(nameof(Reminder.EffectiveDueDate)).Should().NotBeNull();
        typeof(PersonSnapshot).GetProperty(nameof(Person.DisplayName)).Should().BeNull();
        typeof(PersonSnapshot).GetProperty(nameof(Person.Birthday)).Should().BeNull();
        typeof(ReminderSnapshot).GetProperty(nameof(Reminder.EffectiveDueDate)).Should().BeNull();

        Classifications[typeof(UserProfile)][nameof(UserProfile.UnsubscribeToken)].Should().Be("secret");
        Classifications[typeof(UserProfile)][nameof(UserProfile.UnsubscribeTokenVerifier)].Should().Be("secret");
        Classifications[typeof(ProductActivity)][nameof(ProductActivity.ReturnedInDays30To59)].Should().Be("export-only");
        Classifications[typeof(ContactMethod)][nameof(ContactMethod.NormalizedValue)].Should().Be("recomputed");
        Classifications[typeof(InteractionParticipant)][nameof(InteractionParticipant.InteractionId)].Should().Be("graph");
    }

    [Fact]
    public void Parent_owned_links_are_represented_by_nested_snapshot_collections()
    {
        typeof(PersonSnapshot).GetProperty(nameof(PersonSnapshot.ContactMethods)).Should().NotBeNull();
        typeof(PersonSnapshot).GetProperty(nameof(PersonSnapshot.TagIds)).Should().NotBeNull();
        typeof(InteractionSnapshot).GetProperty(nameof(InteractionSnapshot.Participants)).Should().NotBeNull();
    }

    private static IReadOnlyDictionary<string, string> Classify(
        string[] json,
        string[] privateProperties,
        string[]? securityMetadata = null,
        string[]? graph = null,
        string[]? recomputed = null,
        string[]? exportOnly = null,
        string[]? secrets = null)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(json, "json");
        Add(privateProperties, "private");
        Add(securityMetadata ?? [], "security-metadata");
        Add(graph ?? [], "graph");
        Add(recomputed ?? [], "recomputed");
        Add(exportOnly ?? [], "export-only");
        Add(secrets ?? [], "secret");
        return result;

        void Add(IEnumerable<string> properties, string category)
        {
            foreach (var property in properties)
            {
                result.Add(property, category);
            }
        }
    }
}
