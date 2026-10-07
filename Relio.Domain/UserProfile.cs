namespace Relio.Domain;

/// <summary>
/// Per-user settings that are not tied to any one product entity: the user's time zone (epic #12),
/// their optional display name (#18), and whether first-run onboarding has been dismissed (#48).
/// Sign-up (#15), account settings (#18) and the onboarding service (#48) read and write it as the
/// Identity user's profile. Keyed by <see cref="IOwnedEntity.OwnerId"/> like every other
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

    /// <summary>
    /// Whether birthday reminders are enabled for this user (epic #36, issue #38). Defaults to true.
    /// </summary>
    public bool BirthdayRemindersEnabled { get; set; } = true;

    /// <summary>
    /// Default lead time in days for birthday reminders (issue #38). Defaults to 0 (on the day).
    /// </summary>
    public int DefaultBirthdayLeadDays { get; set; } = 0;

    /// <summary>
    /// How the user receives reminder emails (issue #40). Defaults to <see cref="ReminderEmailDelivery.DailyDigest"/>.
    /// </summary>
    public ReminderEmailDelivery ReminderEmailDelivery { get; set; } = ReminderEmailDelivery.DailyDigest;

    /// <summary>
    /// Secure token for one-click unsubscribe links in reminder emails (issue #40).
    /// </summary>
    public string? UnsubscribeToken { get; set; }

    /// <summary>
    /// A SHA-256 verifier used to find the encrypted unsubscribe token without searching its
    /// randomized ciphertext. This is infrastructure metadata and is not user-exportable.
    /// </summary>
    public string? UnsubscribeTokenVerifier { get; set; }

    /// <summary>
    /// Whether the first-run guide has been completed or skipped (issue #48). Defaults to
    /// <see langword="true"/> so legacy, programmatically-created and demo accounts are not
    /// unexpectedly enrolled; account registration explicitly sets this to <see langword="false"/>.
    /// </summary>
    public bool OnboardingDismissed { get; set; } = true;
}
