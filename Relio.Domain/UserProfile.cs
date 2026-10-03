namespace Relio.Domain;

/// <summary>
/// Per-user settings that are not tied to any one product entity. Today this is just the user's
/// time zone (epic #12); account settings (#18) and sign-up (#15) will read and write it as the
/// Identity user's profile. Keyed by <see cref="IOwnedEntity.OwnerId"/> like every other owned
/// entity - see the "User-scoped data pattern" section of AGENTS.md - with a unique index
/// enforcing exactly one profile per user (see <c>Relio.Data.Configurations.UserProfileConfiguration</c>).
/// </summary>
public sealed class UserProfile : OwnedEntity
{
    /// <summary>
    /// The IANA time zone id (e.g. <c>"Europe/Rome"</c>) used to interpret this user's calendar
    /// dates - birthdays, reminders and interaction dates (see the "Dates and time zones" section
    /// of AGENTS.md). Defaults to <c>"UTC"</c> until the user sets one (at sign-up, #15, or later
    /// in account settings, #18). Always a valid id - see
    /// <c>Relio.Application.Time.TimeZoneIds.TryParse</c>, which every write path must validate
    /// against before assigning this property.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";
}
