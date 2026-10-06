using Relio.Domain;

namespace Relio.Application.Profile;

/// <summary>
/// Use cases for the current user's own <see cref="UserProfile"/> settings that are not their time
/// zone (see <c>Relio.Application.Time.IUserTimeZoneService</c> for that): today, the optional
/// display name shown in account settings (#18). Scoped to the signed-in user
/// (<c>ICurrentUser</c>), like every other Application service - see the "User-scoped data
/// pattern" section of AGENTS.md.
/// </summary>
public interface IUserProfileService
{
    /// <summary>
    /// The current user's display name, or <see langword="null"/> when they have not set one
    /// (including when they have no profile row yet).
    /// </summary>
    Task<string?> GetDisplayNameAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the current user's display name. The value is trimmed; <see langword="null"/> or
    /// whitespace clears it. Creates the user's profile (with the default time zone) if it does
    /// not exist yet, and never changes an existing profile's time zone.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="displayName"/> is longer than <see cref="UserProfile.DisplayNameMaxLength"/>
    /// characters after trimming. The profile is left untouched.
    /// </exception>
    Task SetDisplayNameAsync(string? displayName, CancellationToken cancellationToken = default);
}
