using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Relio.Data.Encryption;

/// <summary>
/// Protects persisted private field values with ASP.NET Core Data Protection.
/// </summary>
public interface IDataProtectionFieldProtector
{
    /// <summary>
    /// Identifies the configured protector for EF Core model-cache isolation. It is not a key.
    /// </summary>
    Guid ModelCacheIdentity { get; }

    /// <summary>Returns a protected value, preserving <see langword="null"/>.</summary>
    string? Protect(string? value, string purpose);

    /// <summary>Returns the original value, preserving <see langword="null"/>.</summary>
    string? Unprotect(string? protectedValue, string purpose);
}

/// <summary>
/// The stable, versioned purposes used for individual encrypted columns.
/// </summary>
public static class ProtectedFieldPurposes
{
    /// <summary>The purpose for <c>Person.HowWeMet</c>.</summary>
    public const string PersonHowWeMet = "Relio.FieldProtection.v1.Person.HowWeMet";

    /// <summary>The purpose for <c>Person.Details</c>.</summary>
    public const string PersonDetails = "Relio.FieldProtection.v1.Person.Details";

    /// <summary>The purpose for <c>Note.Text</c>.</summary>
    public const string NoteText = "Relio.FieldProtection.v1.Note.Text";

    /// <summary>The purpose for <c>Interaction.Description</c>.</summary>
    public const string InteractionDescription = "Relio.FieldProtection.v1.Interaction.Description";

    /// <summary>The purpose for <c>Reminder.Title</c>.</summary>
    public const string ReminderTitle = "Relio.FieldProtection.v1.Reminder.Title";

    /// <summary>The purpose for Identity's persisted user-token values.</summary>
    public const string IdentityUserTokenValue = "Relio.FieldProtection.v1.IdentityUserToken.Value";

    /// <summary>The purpose for <c>UserProfile.UnsubscribeToken</c>.</summary>
    public const string UnsubscribeToken = "Relio.FieldProtection.v1.UserProfile.UnsubscribeToken";
}

/// <summary>
/// EF storage formats supported by the database context. Legacy mode is restricted to the
/// explicit backfill path; design-time mode refuses data access.
/// </summary>
public enum FieldProtectionMode
{
    /// <summary>Encrypt on write and authenticate/decrypt on read.</summary>
    Encrypted,

    /// <summary>Read and write raw storage values for the explicit legacy backfill only.</summary>
    LegacyBackfill,

    /// <summary>Build migration metadata without allowing application data reads or writes.</summary>
    DesignTime,
}

/// <summary>
/// Authenticated Data Protection-backed implementation of <see cref="IDataProtectionFieldProtector"/>.
/// </summary>
public sealed class DataProtectionFieldProtector : IDataProtectionFieldProtector
{
    private readonly IDataProtectionProvider _provider;
    private readonly ConcurrentDictionary<string, IDataProtector> _protectors = new(StringComparer.Ordinal);

    /// <summary>Creates a field protector using the application's shared Data Protection provider.</summary>
    public DataProtectionFieldProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        ModelCacheIdentity = Guid.NewGuid();
    }

    /// <inheritdoc />
    public Guid ModelCacheIdentity { get; }

    /// <inheritdoc />
    public string? Protect(string? value, string purpose)
    {
        if (value is null)
        {
            return null;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        try
        {
            return GetProtector(purpose).Protect(value);
        }
        catch (Exception)
        {
            throw new ProtectedFieldProtectionException();
        }
    }

    /// <inheritdoc />
    public string? Unprotect(string? protectedValue, string purpose)
    {
        if (protectedValue is null)
        {
            return null;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        try
        {
            return GetProtector(purpose).Unprotect(protectedValue);
        }
        catch (Exception)
        {
            throw new ProtectedFieldAuthenticationException();
        }
    }

    private IDataProtector GetProtector(string purpose) =>
        _protectors.GetOrAdd(purpose, _provider.CreateProtector);
}

/// <summary>
/// A sanitized failure raised when stored protected data cannot be authenticated with this
/// application's configured key ring.
/// </summary>
public sealed class ProtectedFieldAuthenticationException()
    : CryptographicException("Stored protected data could not be authenticated with the configured key ring.");

/// <summary>A sanitized failure raised when the configured provider cannot protect a field value.</summary>
public sealed class ProtectedFieldProtectionException()
    : CryptographicException("A field value could not be protected with the configured key ring.");

/// <summary>
/// Names the EF shadow property that records whether a row's protected fields are still legacy
/// plaintext or have been converted to the current protected format.
/// </summary>
public static class FieldProtectionSchema
{
    /// <summary>The shadow column used to track each row's data-protection transition.</summary>
    public const string VersionPropertyName = "SensitiveDataProtectionVersion";

    /// <summary>The current protected-storage version.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The version assigned to rows that predate the protected-storage migration.</summary>
    public const int LegacyVersion = 0;

    /// <summary>
    /// Returns a ciphertext limit that covers worst-case UTF-8/base64 expansion and cryptographic
    /// framing for a validated plaintext maximum.
    /// </summary>
    public static int MaxStoredLength(int maxPlaintextLength) =>
        checked((maxPlaintextLength * 5) + 1024);
}
