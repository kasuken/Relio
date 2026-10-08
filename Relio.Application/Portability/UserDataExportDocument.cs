using Relio.Application.People;
using Relio.Domain;

namespace Relio.Application.Portability;

/// <summary>
/// Versioned, explicit interchange format for Relio-owned user data. IDs preserve graph references
/// within this document only; they are not identities in the destination instance.
/// </summary>
public sealed record UserDataExportDocument
{
    /// <summary>The current document format version.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>The schema version of this document.</summary>
    public required int FormatVersion { get; init; }

    /// <summary>When the source generated this document, as a UTC instant.</summary>
    public required DateTime ExportedAtUtc { get; init; }

    /// <summary>The portable product profile settings. Identity and authentication state are excluded.</summary>
    public required UserProfileSnapshot Profile { get; init; }

    /// <summary>Relationship types owned by the source account.</summary>
    public required IReadOnlyList<RelationshipTypeSnapshot> RelationshipTypes { get; init; }

    /// <summary>Tags owned by the source account.</summary>
    public required IReadOnlyList<TagSnapshot> Tags { get; init; }

    /// <summary>People, including archived people, and their ordered contacts and tag references.</summary>
    public required IReadOnlyList<PersonSnapshot> People { get; init; }

    /// <summary>Interactions and their participant links.</summary>
    public required IReadOnlyList<InteractionSnapshot> Interactions { get; init; }

    /// <summary>Notes, including pinned notes.</summary>
    public required IReadOnlyList<NoteSnapshot> Notes { get; init; }

    /// <summary>Reminders, including completion and delivery history.</summary>
    public required IReadOnlyList<ReminderSnapshot> Reminders { get; init; }

    /// <summary>Difficult moments recorded for relationships.</summary>
    public IReadOnlyList<DifficultMomentSnapshot> DifficultMoments { get; init; } = [];

    /// <summary>
    /// The source account's optional product-activity contribution. This is included for access and
    /// erasure portability only and is deliberately never restored into the destination instance.
    /// </summary>
    public required ProductActivitySnapshot? ProductActivity { get; init; }
}

/// <summary>Portable user-profile preferences, excluding local unsubscribe credentials.</summary>
public sealed record UserProfileSnapshot
{
    /// <summary>Source-local profile ID.</summary>
    public required Guid Id { get; init; }

    /// <summary>UTC creation audit instant.</summary>
    public required DateTime CreatedAtUtc { get; init; }

    /// <summary>UTC update audit instant.</summary>
    public required DateTime UpdatedAtUtc { get; init; }

    /// <summary>IANA time-zone identifier used for the user's calendar dates.</summary>
    public required string TimeZoneId { get; init; }

    /// <summary>Optional display name.</summary>
    public required string? DisplayName { get; init; }

    /// <summary>Whether onboarding has been completed.</summary>
    public required bool OnboardingDismissed { get; init; }

    /// <summary>Whether birthday reminders are enabled.</summary>
    public required bool BirthdayRemindersEnabled { get; init; }

    /// <summary>Default birthday reminder lead time.</summary>
    public required int DefaultBirthdayLeadDays { get; init; }

    /// <summary>The source preference; restores intentionally reset it to no email delivery.</summary>
    public required ReminderEmailDelivery ReminderEmailDelivery { get; init; }
}

/// <summary>A user-owned relationship type.</summary>
public sealed record RelationshipTypeSnapshot
{
    /// <summary>Source-local relationship type ID.</summary>
    public required Guid Id { get; init; }
    /// <summary>The relationship type name.</summary>
    public required string Name { get; init; }
    /// <summary>List position.</summary>
    public required int SortOrder { get; init; }
    /// <summary>UTC creation audit instant.</summary>
    public required DateTime CreatedAtUtc { get; init; }
    /// <summary>UTC update audit instant.</summary>
    public required DateTime UpdatedAtUtc { get; init; }
}

/// <summary>A user-owned tag.</summary>
public sealed record TagSnapshot
{
    /// <summary>Source-local tag ID.</summary>
    public required Guid Id { get; init; }
    /// <summary>The tag name.</summary>
    public required string Name { get; init; }
    /// <summary>UTC creation audit instant.</summary>
    public required DateTime CreatedAtUtc { get; init; }
    /// <summary>UTC update audit instant.</summary>
    public required DateTime UpdatedAtUtc { get; init; }
}

/// <summary>A person profile and all fields that belong to it.</summary>
public sealed record PersonSnapshot : IPersonProfileInput
{
    /// <summary>Source-local person ID.</summary>
    public required Guid Id { get; init; }
    /// <summary>UTC creation audit instant.</summary>
    public required DateTime CreatedAtUtc { get; init; }
    /// <summary>UTC update audit instant.</summary>
    public required DateTime UpdatedAtUtc { get; init; }
    /// <summary>First name.</summary>
    public required string FirstName { get; init; }
    /// <summary>Optional last name.</summary>
    public required string? LastName { get; init; }
    /// <summary>Optional nickname.</summary>
    public required string? Nickname { get; init; }
    /// <summary>Optional source-local relationship type ID.</summary>
    public required Guid? RelationshipTypeId { get; init; }
    /// <summary>Birthday day component.</summary>
    public required int? BirthdayDay { get; init; }
    /// <summary>Birthday month component.</summary>
    public required int? BirthdayMonth { get; init; }
    /// <summary>Birthday year component.</summary>
    public required int? BirthdayYear { get; init; }
    /// <summary>Optional how-we-met narrative.</summary>
    public required string? HowWeMet { get; init; }
    /// <summary>Optional private details narrative.</summary>
    public required string? Details { get; init; }
    /// <summary>Whether the person is archived.</summary>
    public required bool IsArchived { get; init; }
    /// <summary>UTC archive instant, or null when active.</summary>
    public required DateTime? ArchivedAtUtc { get; init; }
    /// <summary>Most recent interaction date in the source user's calendar.</summary>
    public required DateOnly? LastContactedOn { get; init; }
    /// <summary>Optional stay-in-touch cadence.</summary>
    public required int? StayInTouchCadenceDays { get; init; }
    /// <summary>Whether birthday reminders are disabled for this person.</summary>
    public required bool BirthdayReminderDisabled { get; init; }
    /// <summary>Per-person birthday reminder lead-time override.</summary>
    public required int? BirthdayReminderLeadDays { get; init; }
    /// <summary>Ordered contact methods.</summary>
    public required IReadOnlyList<ContactMethodSnapshot> ContactMethods { get; init; }
    /// <summary>Source-local tag IDs.</summary>
    public required IReadOnlyList<Guid> TagIds { get; init; }

    IReadOnlyList<ContactMethodInput>? IPersonProfileInput.ContactMethods =>
        ContactMethods?.Select(x => new ContactMethodInput(null, x.Kind, x.Label, x.Value)).ToArray();
    IReadOnlyCollection<Guid>? IPersonProfileInput.TagIds => TagIds;
    IReadOnlyCollection<string>? IPersonProfileInput.NewTagNames => [];
}

/// <summary>A contact method. Its derived comparison key is intentionally not serialized.</summary>
public sealed record ContactMethodSnapshot
{
    /// <summary>Source-local contact method ID.</summary>
    public required Guid Id { get; init; }
    /// <summary>Contact method kind.</summary>
    public required ContactMethodKind Kind { get; init; }
    /// <summary>Optional label.</summary>
    public required string? Label { get; init; }
    /// <summary>Contact detail value.</summary>
    public required string Value { get; init; }
    /// <summary>Position in the person's contact list.</summary>
    public required int SortOrder { get; init; }
    /// <summary>UTC creation audit instant.</summary>
    public required DateTime CreatedAtUtc { get; init; }
    /// <summary>UTC update audit instant.</summary>
    public required DateTime UpdatedAtUtc { get; init; }
}

/// <summary>An interaction and its source-local participant links.</summary>
public sealed record InteractionSnapshot
{
    /// <summary>Source-local interaction ID.</summary>
    public required Guid Id { get; init; }
    /// <summary>UTC creation audit instant.</summary>
    public required DateTime CreatedAtUtc { get; init; }
    /// <summary>UTC update audit instant.</summary>
    public required DateTime UpdatedAtUtc { get; init; }
    /// <summary>Calendar day on which the interaction occurred.</summary>
    public required DateOnly OccurredOn { get; init; }
    /// <summary>Interaction kind.</summary>
    public required InteractionKind Kind { get; init; }
    /// <summary>Private interaction narrative.</summary>
    public required string Description { get; init; }
    /// <summary>People who participated, in source order.</summary>
    public required IReadOnlyList<InteractionParticipantSnapshot> Participants { get; init; }
}

/// <summary>A person-to-interaction participant link.</summary>
public sealed record InteractionParticipantSnapshot
{
    /// <summary>Source-local link ID.</summary>
    public required Guid Id { get; init; }
    /// <summary>Source-local person ID.</summary>
    public required Guid PersonId { get; init; }
    /// <summary>UTC creation audit instant.</summary>
    public required DateTime CreatedAtUtc { get; init; }
    /// <summary>UTC update audit instant.</summary>
    public required DateTime UpdatedAtUtc { get; init; }
}

/// <summary>A private note, including its pin state.</summary>
public sealed record NoteSnapshot
{
    /// <summary>Source-local note ID.</summary>
    public required Guid Id { get; init; }
    /// <summary>Source-local person ID.</summary>
    public required Guid PersonId { get; init; }
    /// <summary>Private note narrative.</summary>
    public required string Text { get; init; }
    /// <summary>Whether the note is pinned.</summary>
    public required bool IsPinned { get; init; }
    /// <summary>UTC creation audit instant.</summary>
    public required DateTime CreatedAtUtc { get; init; }
    /// <summary>UTC update audit instant.</summary>
    public required DateTime UpdatedAtUtc { get; init; }
}

/// <summary>A reminder and its completion, snooze, and delivery history.</summary>
public sealed record ReminderSnapshot
{
    /// <summary>Source-local reminder ID.</summary>
    public required Guid Id { get; init; }
    /// <summary>Source-local person ID.</summary>
    public required Guid PersonId { get; init; }
    /// <summary>Reminder title.</summary>
    public required string Title { get; init; }
    /// <summary>Calendar due date.</summary>
    public required DateOnly DueDate { get; init; }
    /// <summary>Recurrence frequency.</summary>
    public required ReminderFrequency Frequency { get; init; }
    /// <summary>Custom recurrence interval in months, when applicable.</summary>
    public required int? CustomIntervalMonths { get; init; }
    /// <summary>Calendar date through which the reminder is snoozed.</summary>
    public required DateOnly? SnoozedUntilDate { get; init; }
    /// <summary>Whether the reminder is completed.</summary>
    public required bool IsCompleted { get; init; }
    /// <summary>UTC completion instant.</summary>
    public required DateTime? CompletedAtUtc { get; init; }
    /// <summary>Last date the reminder was delivered to the source user.</summary>
    public required DateOnly? LastDeliveredDate { get; init; }
    /// <summary>UTC creation audit instant.</summary>
    public required DateTime CreatedAtUtc { get; init; }
    /// <summary>UTC update audit instant.</summary>
    public required DateTime UpdatedAtUtc { get; init; }
}

/// <summary>A difficult moment snapshot for portability.</summary>
public sealed record DifficultMomentSnapshot
{
    /// <summary>Source-local difficult moment ID.</summary>
    public required Guid Id { get; init; }
    /// <summary>Source-local person ID.</summary>
    public required Guid PersonId { get; init; }
    /// <summary>Calendar date on which the moment occurred.</summary>
    public required DateOnly OccurredOn { get; init; }
    /// <summary>Description of what happened.</summary>
    public required string Description { get; init; }
    /// <summary>Optional trigger.</summary>
    public required string? Trigger { get; init; }
    /// <summary>Optional resolution.</summary>
    public required string? Resolution { get; init; }
    /// <summary>Optional lessons learned.</summary>
    public required string? LessonsLearned { get; init; }
    /// <summary>Status of the moment.</summary>
    public required DifficultMomentStatus Status { get; init; }
    /// <summary>Calendar date when resolved, if applicable.</summary>
    public required DateOnly? ResolvedOn { get; init; }
    /// <summary>Referenced earlier moment ID, if this is a recurrence.</summary>
    public required Guid? RecurrenceOfId { get; init; }
    /// <summary>UTC creation audit instant.</summary>
    public required DateTime CreatedAtUtc { get; init; }
    /// <summary>UTC update audit instant.</summary>
    public required DateTime UpdatedAtUtc { get; init; }
}

/// <summary>Export-only product activity; never copied into another instance's analytics.</summary>
public sealed record ProductActivitySnapshot
{
    /// <summary>Source-local product activity ID.</summary>
    public required Guid Id { get; init; }
    /// <summary>UTC creation audit instant.</summary>
    public required DateTime CreatedAtUtc { get; init; }
    /// <summary>UTC update audit instant.</summary>
    public required DateTime UpdatedAtUtc { get; init; }
    /// <summary>Cohort start UTC calendar date.</summary>
    public required DateOnly CohortStartedOnUtc { get; init; }
    /// <summary>Last-active UTC calendar date.</summary>
    public required DateOnly LastActiveOnUtc { get; init; }
    /// <summary>Whether the owner returned during day 30 through day 59.</summary>
    public required bool ReturnedInDays30To59 { get; init; }
    /// <summary>UTC instant after which the observation expires.</summary>
    public required DateTime RetentionExpiresAtUtc { get; init; }
}

/// <summary>A minimized vCard person: contact information only, with no private narratives.</summary>
public sealed record VCardPerson
{
    /// <summary>First name.</summary>
    public required string FirstName { get; init; }
    /// <summary>Optional last name.</summary>
    public required string? LastName { get; init; }
    /// <summary>Optional nickname.</summary>
    public required string? Nickname { get; init; }
    /// <summary>Birthday day component.</summary>
    public required int? BirthdayDay { get; init; }
    /// <summary>Birthday month component.</summary>
    public required int? BirthdayMonth { get; init; }
    /// <summary>Birthday year component.</summary>
    public required int? BirthdayYear { get; init; }
    /// <summary>Ordered contact methods.</summary>
    public required IReadOnlyList<VCardContactMethod> ContactMethods { get; init; }
}

/// <summary>A minimized contact method for a vCard.</summary>
public sealed record VCardContactMethod(ContactMethodKind Kind, string? Label, string Value, int SortOrder);
