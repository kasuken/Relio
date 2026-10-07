using Relio.Domain;

namespace Relio.Application.Portability;

/// <summary>Reads and restores the signed-in user's portable data.</summary>
public interface IUserDataPortabilityService
{
    /// <summary>Builds a complete, owner-scoped snapshot of the current user's portable data.</summary>
    Task<UserDataExportDocument> ExportAsync(CancellationToken cancellationToken = default);

    /// <summary>Builds a contact-only vCard for the current user's people.</summary>
    Task<string> ExportPeopleVCardAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a validated snapshot into a registration-only account. Existing user content is
    /// never overwritten.
    /// </summary>
    Task<UserDataRestoreResult> RestoreAsync(
        UserDataExportDocument document,
        CancellationToken cancellationToken = default);
}

/// <summary>The result of a JSON restore.</summary>
public sealed record UserDataRestoreResult(int People, int Interactions, int Notes, int Reminders, int Tags, int RelationshipTypes);

/// <summary>Stable validation and state errors returned by the portability boundary.</summary>
public enum UserDataPortabilityError
{
    InvalidDocument,
    UnsupportedVersion,
    TooManyRows,
    DuplicateId,
    InvalidReference,
    InvalidValue,
    InvalidAuditDate,
    InvalidTimeZone,
    DestinationNotFresh,
}

/// <summary>An expected data-portability error with codes only, never submitted content.</summary>
public sealed class UserDataPortabilityException : Exception
{
    /// <summary>Creates an exception for one or more stable error codes.</summary>
    public UserDataPortabilityException(IReadOnlyList<UserDataPortabilityError> errors)
        : base(string.Join(", ", errors))
    {
        Errors = errors;
    }

    /// <summary>The stable validation or restore-state error codes.</summary>
    public IReadOnlyList<UserDataPortabilityError> Errors { get; }
}
