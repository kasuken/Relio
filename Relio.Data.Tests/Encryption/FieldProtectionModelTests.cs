using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Relio.Data.Encryption;
using Relio.Data.Identity;
using Relio.Domain;

namespace Relio.Data.Tests.Encryption;

public sealed class FieldProtectionModelTests
{
    [Fact]
    public void Model_protects_the_issue_57_field_map_and_keeps_search_metadata_readable()
    {
        using var dbContext = CreateDbContext();

        IsProtected<Person>(dbContext, nameof(Person.HowWeMet));
        IsProtected<Person>(dbContext, nameof(Person.Details));
        IsProtected<Note>(dbContext, nameof(Note.Text));
        IsProtected<Interaction>(dbContext, nameof(Interaction.Description));
        IsProtected<Reminder>(dbContext, nameof(Reminder.Title));
        IsProtected<UserProfile>(dbContext, nameof(UserProfile.UnsubscribeToken));
        IsProtected<IdentityUserToken<string>>(dbContext, nameof(IdentityUserToken<string>.Value));

        IsNotProtected<Person>(dbContext, nameof(Person.FirstName));
        IsNotProtected<Person>(dbContext, nameof(Person.LastName));
        IsNotProtected<Person>(dbContext, nameof(Person.Nickname));
        IsNotProtected<Person>(dbContext, nameof(Person.RelationshipTypeId));
        IsNotProtected<Person>(dbContext, nameof(Person.BirthdayYear));
        IsNotProtected<Person>(dbContext, nameof(Person.BirthdayMonth));
        IsNotProtected<Person>(dbContext, nameof(Person.BirthdayDay));
        IsNotProtected<Person>(dbContext, nameof(Person.LastContactedOn));
        IsNotProtected<Person>(dbContext, nameof(Person.OwnerId));
        IsNotProtected<Person>(dbContext, nameof(Person.CreatedAtUtc));
        IsNotProtected<Person>(dbContext, nameof(Person.UpdatedAtUtc));
        IsNotProtected<Note>(dbContext, nameof(Note.OwnerId));
        IsNotProtected<Note>(dbContext, nameof(Note.PersonId));
        IsNotProtected<Note>(dbContext, nameof(Note.CreatedAtUtc));
        IsNotProtected<Interaction>(dbContext, nameof(Interaction.OccurredOn));
        IsNotProtected<Interaction>(dbContext, nameof(Interaction.OwnerId));
        IsNotProtected<Reminder>(dbContext, nameof(Reminder.PersonId));
        IsNotProtected<Reminder>(dbContext, nameof(Reminder.DueDate));
        IsNotProtected<Reminder>(dbContext, nameof(Reminder.SnoozedUntilDate));
        IsNotProtected<Reminder>(dbContext, nameof(Reminder.CompletedAtUtc));
        IsNotProtected<Reminder>(dbContext, nameof(Reminder.LastDeliveredDate));
        IsNotProtected<Reminder>(dbContext, nameof(Reminder.OwnerId));
        IsNotProtected<UserProfile>(dbContext, nameof(UserProfile.TimeZoneId));
        IsNotProtected<UserProfile>(dbContext, nameof(UserProfile.DisplayName));
        IsNotProtected<UserProfile>(dbContext, nameof(UserProfile.UnsubscribeTokenVerifier));
        IsNotProtected<RelioUser>(dbContext, nameof(RelioUser.PasswordHash));
        IsNotProtected<ContactMethod>(dbContext, nameof(ContactMethod.Label));
        IsNotProtected<ContactMethod>(dbContext, nameof(ContactMethod.Value));
        IsNotProtected<ContactMethod>(dbContext, nameof(ContactMethod.NormalizedValue));
        IsNotProtected<ContactMethod>(dbContext, nameof(ContactMethod.OwnerId));
        IsNotProtected<ContactMethod>(dbContext, nameof(ContactMethod.PersonId));
        IsNotProtected<Tag>(dbContext, nameof(Tag.Name));
        IsNotProtected<RelationshipType>(dbContext, nameof(RelationshipType.Name));
        EnumIsStoredReadably<ContactMethod, ContactMethodKind>(
            dbContext,
            nameof(ContactMethod.Kind),
            ContactMethodKind.Email,
            "Email");
        EnumIsStoredReadably<Interaction, InteractionKind>(
            dbContext,
            nameof(Interaction.Kind),
            InteractionKind.Call,
            "Call");
        EnumIsStoredReadably<UserProfile, ReminderEmailDelivery>(
            dbContext,
            nameof(UserProfile.ReminderEmailDelivery),
            ReminderEmailDelivery.DailyDigest,
            "DailyDigest");
        IsNotProtected<IdentityUserToken<string>>(dbContext, nameof(IdentityUserToken<string>.UserId));
        IsNotProtected<IdentityUserToken<string>>(dbContext, nameof(IdentityUserToken<string>.LoginProvider));
        IsNotProtected<IdentityUserToken<string>>(dbContext, nameof(IdentityUserToken<string>.Name));

        HasVersionConcurrencyToken<Person>(dbContext);
        HasVersionConcurrencyToken<Note>(dbContext);
        HasVersionConcurrencyToken<Interaction>(dbContext);
        HasVersionConcurrencyToken<Reminder>(dbContext);
        HasVersionConcurrencyToken<UserProfile>(dbContext);
        HasVersionConcurrencyToken<IdentityUserToken<string>>(dbContext);
    }

    [Fact]
    public void Encrypted_columns_allow_authenticated_overhead_without_changing_plaintext_limits()
    {
        using var dbContext = CreateDbContext();

        MaximumStoredLength<Person>(dbContext, nameof(Person.HowWeMet), Person.HowWeMetMaxLength);
        MaximumStoredLength<Person>(dbContext, nameof(Person.Details), Person.DetailsMaxLength);
        MaximumStoredLength<Note>(dbContext, nameof(Note.Text), Note.TextMaxLength);
        MaximumStoredLength<Interaction>(dbContext, nameof(Interaction.Description), Interaction.DescriptionMaxLength);
        MaximumStoredLength<Reminder>(dbContext, nameof(Reminder.Title), Reminder.TitleMaxLength);
        MaximumStoredLength<UserProfile>(dbContext, nameof(UserProfile.UnsubscribeToken), 64);
    }

    [Fact]
    public void Unsubscribe_lookup_indexes_only_the_stable_verifier()
    {
        using var dbContext = CreateDbContext();
        var indexes = dbContext.Model.FindEntityType(typeof(UserProfile))!.GetIndexes();

        indexes.Should().Contain(index => index.Properties.Any(
            property => property.Name == nameof(UserProfile.UnsubscribeTokenVerifier)));
        indexes.Should().NotContain(index => index.Properties.Any(
            property => property.Name == nameof(UserProfile.UnsubscribeToken)));
    }

    private static void IsProtected<TEntity>(RelioDbContext dbContext, string propertyName)
        where TEntity : class =>
        dbContext.Model.FindEntityType(typeof(TEntity))!
            .FindProperty(propertyName)!
            .GetValueConverter()
            .Should().NotBeNull();

    private static void IsNotProtected<TEntity>(RelioDbContext dbContext, string propertyName)
        where TEntity : class =>
        dbContext.Model.FindEntityType(typeof(TEntity))!
            .FindProperty(propertyName)!
            .GetValueConverter()
            .Should().BeNull();

    private static void MaximumStoredLength<TEntity>(
        RelioDbContext dbContext,
        string propertyName,
        int maximumPlaintextLength)
        where TEntity : class =>
        dbContext.Model.FindEntityType(typeof(TEntity))!
            .FindProperty(propertyName)!
            .GetMaxLength()
            .Should().Be(FieldProtectionSchema.MaxStoredLength(maximumPlaintextLength));

    private static void EnumIsStoredReadably<TEntity, TEnum>(
        RelioDbContext dbContext,
        string propertyName,
        TEnum value,
        string expectedStorageValue)
        where TEntity : class
        where TEnum : struct, Enum
    {
        var converter = dbContext.Model.FindEntityType(typeof(TEntity))!
            .FindProperty(propertyName)!
            .GetTypeMapping()
            .Converter;
        converter.Should().NotBeNull();
        converter!.ConvertToProvider(value).Should().Be(expectedStorageValue);
    }

    private static void HasVersionConcurrencyToken<TEntity>(RelioDbContext dbContext)
        where TEntity : class
    {
        var property = dbContext.Model.FindEntityType(typeof(TEntity))!
            .FindProperty(FieldProtectionSchema.VersionPropertyName)!;
        property.IsShadowProperty().Should().BeTrue();
        property.IsConcurrencyToken.Should().BeTrue();
    }

    private static RelioDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new RelioDbContext(options, TimeProvider.System, FieldProtector);
    }
}
