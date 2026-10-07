using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Relio.Data.Encryption;
using Relio.Data.Identity;
using Relio.Domain;

namespace Relio.Data.Tests.Encryption;

public sealed class FieldProtectionTests
{
    [Fact]
    public void Protect_and_unprotect_preserves_null_empty_unicode_and_maximum_note_text()
    {
        using var keyRing = CreateIsolatedKeyRing();
        var protector = keyRing.FieldProtector;
        var maximumText = string.Concat(
            new string('x', Note.TextMaxLength - 4),
            "🌿e\u0301");
        var maximumAstralText = string.Concat(Enumerable.Repeat("🌿", Note.TextMaxLength / 2));
        var maximumThreeByteText = new string('\u0800', Note.TextMaxLength);

        var protectedEmpty = protector.Protect(string.Empty, ProtectedFieldPurposes.NoteText);
        var protectedUnicode = protector.Protect("naïve 東京 🌿 e\u0301", ProtectedFieldPurposes.NoteText);
        var protectedMaximum = protector.Protect(maximumText, ProtectedFieldPurposes.NoteText);
        var protectedMaximumAstral = protector.Protect(maximumAstralText, ProtectedFieldPurposes.NoteText);
        var protectedMaximumThreeByte = protector.Protect(maximumThreeByteText, ProtectedFieldPurposes.NoteText);

        protector.Protect(null, ProtectedFieldPurposes.NoteText).Should().BeNull();
        protector.Unprotect(null, ProtectedFieldPurposes.NoteText).Should().BeNull();
        protectedEmpty.Should().NotBeNullOrEmpty();
        protectedEmpty.Should().NotBe(string.Empty);
        protector.Unprotect(protectedEmpty, ProtectedFieldPurposes.NoteText).Should().BeEmpty();
        protector.Unprotect(protectedUnicode, ProtectedFieldPurposes.NoteText).Should().Be("naïve 東京 🌿 e\u0301");
        protector.Unprotect(protectedMaximum, ProtectedFieldPurposes.NoteText).Should().Be(maximumText);
        protector.Unprotect(protectedMaximumAstral, ProtectedFieldPurposes.NoteText).Should().Be(maximumAstralText);
        protector.Unprotect(protectedMaximumThreeByte, ProtectedFieldPurposes.NoteText).Should().Be(maximumThreeByteText);
        maximumText.Length.Should().Be(Note.TextMaxLength);
        maximumAstralText.Length.Should().Be(Note.TextMaxLength);
        maximumThreeByteText.Length.Should().Be(Note.TextMaxLength);
        protectedMaximumThreeByte!.Length.Should().BeLessThanOrEqualTo(
            FieldProtectionSchema.MaxStoredLength(Note.TextMaxLength));
    }

    [Fact]
    public void Protector_purposes_and_key_rings_are_isolated()
    {
        using var firstKeyRing = CreateIsolatedKeyRing();
        using var otherKeyRing = CreateIsolatedKeyRing();
        var protectedValue = firstKeyRing.FieldProtector.Protect(
            "private narrative",
            ProtectedFieldPurposes.PersonDetails);

        firstKeyRing.FieldProtector
            .Unprotect(protectedValue, ProtectedFieldPurposes.PersonDetails)
            .Should().Be("private narrative");
        var wrongPurpose = () => firstKeyRing.FieldProtector.Unprotect(
            protectedValue,
            ProtectedFieldPurposes.NoteText);
        var wrongKey = () => otherKeyRing.FieldProtector.Unprotect(
            protectedValue,
            ProtectedFieldPurposes.PersonDetails);

        wrongPurpose.Should().Throw<ProtectedFieldAuthenticationException>();
        wrongKey.Should().Throw<ProtectedFieldAuthenticationException>();
    }

    [Fact]
    public void Context_maps_each_issue_57_field_to_its_own_versioned_purpose()
    {
        using var keyRing = CreateIsolatedKeyRing();
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        using var dbContext = new RelioDbContext(options, TimeProvider.System, keyRing.FieldProtector);

        var howWeMet = AssertProtectedConverter<Person>(
            dbContext,
            keyRing.FieldProtector,
            nameof(Person.HowWeMet),
            ProtectedFieldPurposes.PersonHowWeMet,
            "private meeting story");
        var details = AssertProtectedConverter<Person>(
            dbContext,
            keyRing.FieldProtector,
            nameof(Person.Details),
            ProtectedFieldPurposes.PersonDetails,
            "private person details");
        AssertProtectedConverter<Note>(
            dbContext,
            keyRing.FieldProtector,
            nameof(Note.Text),
            ProtectedFieldPurposes.NoteText,
            "private note");
        AssertProtectedConverter<Interaction>(
            dbContext,
            keyRing.FieldProtector,
            nameof(Interaction.Description),
            ProtectedFieldPurposes.InteractionDescription,
            "private interaction");
        AssertProtectedConverter<Reminder>(
            dbContext,
            keyRing.FieldProtector,
            nameof(Reminder.Title),
            ProtectedFieldPurposes.ReminderTitle,
            "private reminder");
        AssertProtectedConverter<UserProfile>(
            dbContext,
            keyRing.FieldProtector,
            nameof(UserProfile.UnsubscribeToken),
            ProtectedFieldPurposes.UnsubscribeToken,
            "synthetic-unsubscribe-token");
        AssertProtectedConverter<IdentityUserToken<string>>(
            dbContext,
            keyRing.FieldProtector,
            nameof(IdentityUserToken<string>.Value),
            ProtectedFieldPurposes.IdentityUserTokenValue,
            "synthetic-authenticator-value");

        var wrongPurpose = () => details.Converter.ConvertFromProvider(howWeMet.Ciphertext);
        wrongPurpose.Should().Throw<ProtectedFieldAuthenticationException>();
    }

    [Fact]
    public void Durable_key_ring_restarts_and_rotates_without_losing_old_values()
    {
        using var original = CreateIsolatedKeyRing();
        var oldCiphertext = original.FieldProtector.Protect(
            "survives restart and rotation",
            ProtectedFieldPurposes.InteractionDescription);
        var now = TimeProvider.System.GetUtcNow();
        original.KeyManager
            .CreateNewKey(now.AddMinutes(-1), now.AddDays(30));

        using var restarted = original.Restart();

        restarted.FieldProtector
            .Unprotect(oldCiphertext, ProtectedFieldPurposes.InteractionDescription)
            .Should().Be("survives restart and rotation");
    }

    [Fact]
    public void Missing_key_ring_fails_closed_for_existing_ciphertext()
    {
        using var original = CreateIsolatedKeyRing();
        var ciphertext = original.FieldProtector.Protect("secret", ProtectedFieldPurposes.NoteText);
        Directory.Delete(original.KeyDirectory, recursive: true);

        using var restarted = original.Restart();
        var read = () => restarted.FieldProtector.Unprotect(ciphertext, ProtectedFieldPurposes.NoteText);

        read.Should().Throw<ProtectedFieldAuthenticationException>();
    }

    [Fact]
    public void Corrupt_key_ring_fails_closed_for_existing_ciphertext()
    {
        using var original = CreateIsolatedKeyRing();
        var ciphertext = original.FieldProtector.Protect("secret", ProtectedFieldPurposes.NoteText);
        var keyFiles = Directory.GetFiles(original.KeyDirectory, "*.xml");
        keyFiles.Should().ContainSingle();
        File.WriteAllText(keyFiles[0], "not a valid Data Protection key document");

        using var restarted = original.Restart();
        var read = () => restarted.FieldProtector.Unprotect(ciphertext, ProtectedFieldPurposes.NoteText);

        read.Should().Throw<ProtectedFieldAuthenticationException>();
    }

    [Fact]
    public void Different_contexts_do_not_reuse_a_converter_captured_from_another_key_ring()
    {
        using var firstKeyRing = CreateIsolatedKeyRing();
        using var wrongKeyRing = CreateIsolatedKeyRing();
        var options = new DbContextOptionsBuilder<RelioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var firstContext = new RelioDbContext(options, TimeProvider.System, firstKeyRing.FieldProtector);
        using var wrongContext = new RelioDbContext(options, TimeProvider.System, wrongKeyRing.FieldProtector);
        var firstConverter = GetProtectionConverter<Person>(firstContext, nameof(Person.HowWeMet));
        var wrongConverter = GetProtectionConverter<Person>(wrongContext, nameof(Person.HowWeMet));
        var firstCiphertext = (string)firstConverter.ConvertToProvider("A protected private story.")!;
        var secondCiphertext = (string)wrongConverter.ConvertToProvider("A second private story.")!;

        wrongConverter.ConvertFromProvider(secondCiphertext).Should().Be("A second private story.");
        var firstKeyCannotReadSecondValue = () => firstConverter.ConvertFromProvider(secondCiphertext);
        var secondKeyCannotReadFirstValue = () => wrongConverter.ConvertFromProvider(firstCiphertext);
        firstKeyCannotReadSecondValue.Should().Throw<ProtectedFieldAuthenticationException>();
        secondKeyCannotReadFirstValue.Should().Throw<ProtectedFieldAuthenticationException>();
    }

    private static (ValueConverter Converter, string Ciphertext) AssertProtectedConverter<TEntity>(
        RelioDbContext dbContext,
        IDataProtectionFieldProtector protector,
        string propertyName,
        string purpose,
        string plaintext)
        where TEntity : class
    {
        var converter = GetProtectionConverter<TEntity>(dbContext, propertyName);
        var providerValue = converter.ConvertToProvider(plaintext);
        providerValue.Should().BeOfType<string>();
        var ciphertext = (string)providerValue!;

        ciphertext.Should().NotBe(plaintext);
        converter.ConvertFromProvider(ciphertext).Should().Be(plaintext);
        protector.Unprotect(ciphertext, purpose).Should().Be(plaintext);
        return (converter, ciphertext);
    }

    private static ValueConverter GetProtectionConverter<TEntity>(
        RelioDbContext dbContext,
        string propertyName)
        where TEntity : class
    {
        var converter = dbContext.Model.FindEntityType(typeof(TEntity))!
            .FindProperty(propertyName)!
            .GetTypeMapping()
            .Converter;
        converter.Should().NotBeNull();
        return converter!;
    }
}
