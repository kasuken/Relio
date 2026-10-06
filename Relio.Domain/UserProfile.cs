namespace Relio.Domain;

/// <summary>
/// Per-user settings that are not tied to any one product entity: the user's time zone (epic #12)
/// and their optional display name (#18). Sign-up (#15) and account settings (#18) read and write
/// it as the Identity user's profile. Keyed by <see cref="IOwnedEntity.OwnerId"/> like every other
/// owned entity - see the "User-scoped data pattern" section of AGENTS.md - with a unique index
/// enforcing exactly one profile per user (see <c>Relio.Data.Configurations.UserProfileConfiguration</c>).
/// </summary>
public sealed class UserProfile : OwnedEntity
{
    /// <summary>The longest <see cref="DisplayName"/> Relio stores, in characters.</summary>
    public const int DisplayNameMaxLength = 100;

    /// <summary>
    /// The IANA time zone id (e.g. <c>"Europe/Rome"</c>) used to interpret this user's calendar
    /// dates - birthdays, reminders and interaction dates (see the "Dates and time zones" section
    /// of AGENTS.md). Defaults to <c>"UTC"</c> until the user sets one (at sign-up, #15, or later
    /// in account settings, #18). Always a valid id - see
    /// <c>Relio.Application.Time.TimeZoneIds.TryParse</c>, which every write path must validate
    /// against before assigning this property.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>
    /// How Relio addresses the user (account settings, #18). Optional: <see langword="null"/>
    /// means the user has not set one. Never empty or whitespace-only and never longer than
    /// <see cref="DisplayNameMaxLength"/> - <c>Relio.Application.Profile.IUserProfileService</c>
    /// trims and normalizes it before it is assigned.
    /// </summary>
    public string? DisplayName { get; set; }
}
