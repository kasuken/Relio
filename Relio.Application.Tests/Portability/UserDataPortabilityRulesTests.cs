using Relio.Application.Portability;
using Relio.Domain;

namespace Relio.Application.Tests.Portability;

public sealed class UserDataPortabilityRulesTests
{
    private static readonly DateTimeOffset Now = new(2025, 2, 1, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Validate_accepts_a_year_birthday_that_is_today_in_the_imported_time_zone()
    {
        var document = ValidDocument("Pacific/Kiritimati") with
        {
            People = [ValidPerson() with { BirthdayYear = 2025, BirthdayMonth = 2, BirthdayDay = 2 }],
        };

        UserDataPortabilityRules.Validate(document, Now).Should().BeEmpty();
    }

    [Fact]
    public void Validate_rejects_a_birthday_that_is_future_in_the_imported_time_zone()
    {
        var document = ValidDocument("Pacific/Pago_Pago") with
        {
            People = [ValidPerson() with { BirthdayYear = 2025, BirthdayMonth = 2, BirthdayDay = 1 }],
        };

        UserDataPortabilityRules.Validate(document, Now)
            .Should().Contain(UserDataPortabilityError.InvalidValue);
    }

    [Fact]
    public void Validate_accepts_an_archived_person_without_an_archive_timestamp()
    {
        var document = ValidDocument() with
        {
            People = [ValidPerson() with { IsArchived = true }],
        };

        UserDataPortabilityRules.Validate(document, Now).Should().BeEmpty();
    }

    [Fact]
    public void Validate_rejects_an_active_person_with_an_archive_timestamp()
    {
        var document = ValidDocument() with
        {
            People =
            [
                ValidPerson() with { ArchivedAtUtc = Now.UtcDateTime.AddHours(-1) },
            ],
        };

        UserDataPortabilityRules.Validate(document, Now)
            .Should().Contain(UserDataPortabilityError.InvalidValue);
    }

    [Fact]
    public void Validate_rejects_a_null_nested_record_as_an_invalid_document_not_a_runtime_error()
    {
        var document = ValidDocument() with { People = [null!] };

        var result = UserDataPortabilityRules.Validate(document, Now);
        result.Should().Contain(UserDataPortabilityError.InvalidDocument);
    }

    [Fact]
    public void Validate_rejects_duplicate_tag_links_and_dangling_graph_references()
    {
        var tagId = Guid.NewGuid();
        var person = ValidPerson() with { TagIds = [tagId, tagId] };
        var document = ValidDocument() with
        {
            People = [person],
            Tags = [new TagSnapshot
            {
                Id = tagId,
                Name = "Family",
                CreatedAtUtc = Now.UtcDateTime.AddDays(-1),
                UpdatedAtUtc = Now.UtcDateTime,
            }],
            Interactions =
            [
                new InteractionSnapshot
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = Now.UtcDateTime.AddDays(-1),
                    UpdatedAtUtc = Now.UtcDateTime,
                    OccurredOn = new DateOnly(2025, 1, 31),
                    Kind = InteractionKind.Call,
                    Description = "A useful conversation.",
                    Participants =
                    [
                        new InteractionParticipantSnapshot
                        {
                            Id = Guid.NewGuid(),
                            PersonId = Guid.NewGuid(),
                            CreatedAtUtc = Now.UtcDateTime.AddDays(-1),
                            UpdatedAtUtc = Now.UtcDateTime,
                        },
                    ],
                },
            ],
        };

        var errors = UserDataPortabilityRules.Validate(document, Now);

        errors.Should().Contain(UserDataPortabilityError.InvalidValue);
        errors.Should().Contain(UserDataPortabilityError.InvalidReference);
    }

    [Fact]
    public void Validate_rejects_reused_record_ids_within_an_entity_type()
    {
        var tagId = Guid.NewGuid();
        var document = ValidDocument() with
        {
            Tags =
            [
                new TagSnapshot
                {
                    Id = tagId,
                    Name = "Work",
                    CreatedAtUtc = Now.UtcDateTime.AddDays(-1),
                    UpdatedAtUtc = Now.UtcDateTime,
                },
                new TagSnapshot
                {
                    Id = tagId,
                    Name = "Home",
                    CreatedAtUtc = Now.UtcDateTime.AddDays(-1),
                    UpdatedAtUtc = Now.UtcDateTime,
                },
            ],
        };

        UserDataPortabilityRules.Validate(document, Now)
            .Should().Contain(UserDataPortabilityError.DuplicateId);
    }

    [Fact]
    public void Validate_rejects_undefined_interaction_and_reminder_enum_values()
    {
        var person = ValidPerson();
        var auditStart = Now.UtcDateTime.AddDays(-1);
        var document = ValidDocument() with
        {
            People = [person],
            Interactions =
            [
                new InteractionSnapshot
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = auditStart,
                    UpdatedAtUtc = Now.UtcDateTime,
                    OccurredOn = new DateOnly(2025, 1, 31),
                    Kind = (InteractionKind)999,
                    Description = "A conversation.",
                    Participants =
                    [
                        new InteractionParticipantSnapshot
                        {
                            Id = Guid.NewGuid(),
                            PersonId = person.Id,
                            CreatedAtUtc = auditStart,
                            UpdatedAtUtc = Now.UtcDateTime,
                        },
                    ],
                },
            ],
            Reminders =
            [
                new ReminderSnapshot
                {
                    Id = Guid.NewGuid(),
                    PersonId = person.Id,
                    Title = "Reconnect",
                    DueDate = new DateOnly(2025, 1, 31),
                    Frequency = (ReminderFrequency)999,
                    CustomIntervalMonths = null,
                    SnoozedUntilDate = null,
                    IsCompleted = false,
                    CompletedAtUtc = null,
                    LastDeliveredDate = null,
                    CreatedAtUtc = auditStart,
                    UpdatedAtUtc = Now.UtcDateTime,
                },
            ],
        };

        UserDataPortabilityRules.Validate(document, Now)
            .Should().Contain(UserDataPortabilityError.InvalidValue);
    }

    [Fact]
    public void Validate_stops_on_documents_over_the_row_limit()
    {
        var document = ValidDocument() with
        {
            Notes = Enumerable.Range(0, UserDataPortabilityRules.MaxTotalRows)
                .Select(_ => (NoteSnapshot)null!)
                .ToArray(),
        };

        UserDataPortabilityRules.Validate(document, Now)
            .Should().ContainSingle().Which.Should().Be(UserDataPortabilityError.TooManyRows);
    }

    private static UserDataExportDocument ValidDocument(string timeZoneId = "UTC") => new()
    {
        FormatVersion = UserDataExportDocument.CurrentFormatVersion,
        ExportedAtUtc = Now.UtcDateTime,
        Profile = new UserProfileSnapshot
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = Now.UtcDateTime.AddDays(-1),
            UpdatedAtUtc = Now.UtcDateTime,
            TimeZoneId = timeZoneId,
            DisplayName = null,
            OnboardingDismissed = true,
            BirthdayRemindersEnabled = true,
            DefaultBirthdayLeadDays = 0,
            ReminderEmailDelivery = ReminderEmailDelivery.DailyDigest,
        },
        RelationshipTypes = [],
        Tags = [],
        People = [],
        Interactions = [],
        Notes = [],
        Reminders = [],
        ProductActivity = null,
    };

    private static PersonSnapshot ValidPerson() => new()
    {
        Id = Guid.NewGuid(),
        CreatedAtUtc = Now.UtcDateTime.AddDays(-1),
        UpdatedAtUtc = Now.UtcDateTime,
        FirstName = "Ada",
        LastName = null,
        Nickname = null,
        RelationshipTypeId = null,
        BirthdayDay = null,
        BirthdayMonth = null,
        BirthdayYear = null,
        HowWeMet = null,
        Details = null,
        IsArchived = false,
        ArchivedAtUtc = null,
        LastContactedOn = null,
        StayInTouchCadenceDays = null,
        BirthdayReminderDisabled = false,
        BirthdayReminderLeadDays = null,
        ContactMethods = [],
        TagIds = [],
    };
}
