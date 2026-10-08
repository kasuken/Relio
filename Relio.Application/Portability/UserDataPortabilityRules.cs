using Relio.Application.DifficultMoments;
using Relio.Application.Interactions;
using Relio.Application.Metrics;
using Relio.Application.Notes;
using Relio.Application.People;
using Relio.Application.Reminders;
using Relio.Application.Time;
using Relio.Domain;

namespace Relio.Application.Portability;

/// <summary>Pure, whole-document validation for an imported portability snapshot.</summary>
public static class UserDataPortabilityRules
{
    /// <summary>Maximum total source rows accepted in one document.</summary>
    public const int MaxTotalRows = 100_000;

    /// <summary>Maximum people accepted in one document.</summary>
    public const int MaxPeople = 20_000;

    /// <summary>Maximum aggregate interaction rows accepted in one document.</summary>
    public const int MaxInteractions = 100_000;

    /// <summary>
    /// Validates structure, references, dates, unique names, and the same rules used by ordinary
    /// entity services. The supplied instant is explicit so callers and tests control calendar
    /// validation without a hidden clock.
    /// </summary>
    public static IReadOnlyList<UserDataPortabilityError> Validate(
        UserDataExportDocument? document,
        DateTimeOffset nowUtc)
    {
        if (document is null)
        {
            return [UserDataPortabilityError.InvalidDocument];
        }

        var errors = new HashSet<UserDataPortabilityError>();
        if (document.FormatVersion != UserDataExportDocument.CurrentFormatVersion)
        {
            errors.Add(UserDataPortabilityError.UnsupportedVersion);
        }

        if (!IsUtc(nowUtc.UtcDateTime) || !IsUtc(document.ExportedAtUtc) || document.ExportedAtUtc > nowUtc.UtcDateTime.AddMinutes(5))
        {
            errors.Add(UserDataPortabilityError.InvalidAuditDate);
        }

        if (document.Profile is null
            || document.RelationshipTypes is null
            || document.Tags is null
            || document.People is null
            || document.Interactions is null
            || document.Notes is null
            || document.Reminders is null
            || document.DifficultMoments is null)
        {
            errors.Add(UserDataPortabilityError.InvalidDocument);
            return errors.ToArray();
        }

        if (document.People.Count > MaxPeople
            || document.Interactions.Count > MaxInteractions
            || TotalRows(document) > MaxTotalRows)
        {
            errors.Add(UserDataPortabilityError.TooManyRows);
            return errors.ToArray();
        }

        if (HasInvalidStructure(document))
        {
            errors.Add(UserDataPortabilityError.InvalidDocument);
            return errors.ToArray();
        }

        ValidateProfile(document.Profile, errors);
        ValidateAudit(document.Profile.CreatedAtUtc, document.Profile.UpdatedAtUtc, document.ExportedAtUtc, errors);

        var ids = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        ValidateUniqueIds(document, ids, errors);
        ValidateAudits(document, errors);
        ValidateReferencesAndValues(document, nowUtc, errors);
        ValidateProductActivity(document.ProductActivity, document.ExportedAtUtc, nowUtc, errors);
        ValidateUniqueNames(document, errors);

        return errors.ToArray();
    }

    private static void ValidateProfile(UserProfileSnapshot profile, HashSet<UserDataPortabilityError> errors)
    {
        if (string.IsNullOrWhiteSpace(profile.TimeZoneId)
            || profile.TimeZoneId.Length > 100
            || !TimeZoneIds.TryParse(profile.TimeZoneId, out _))
        {
            errors.Add(UserDataPortabilityError.InvalidTimeZone);
        }

        if (profile.Id == Guid.Empty
            || profile.DisplayName?.Length > UserProfile.DisplayNameMaxLength
            || profile.DisplayName is not null && string.IsNullOrWhiteSpace(profile.DisplayName)
            || profile.DefaultBirthdayLeadDays < 0
            || !Enum.IsDefined(profile.ReminderEmailDelivery))
        {
            errors.Add(UserDataPortabilityError.InvalidValue);
        }
    }

    private static bool HasInvalidStructure(UserDataExportDocument document) =>
        document.RelationshipTypes.Any(item => item is null)
        || document.Tags.Any(item => item is null)
        || document.People.Any(person =>
            person is null
            || person.ContactMethods is null
            || person.ContactMethods.Any(contact => contact is null)
            || person.TagIds is null)
        || document.Interactions.Any(interaction =>
            interaction is null
            || interaction.Participants is null
            || interaction.Participants.Any(participant => participant is null))
        || document.Notes.Any(note => note is null)
        || document.Reminders.Any(reminder => reminder is null)
        || document.DifficultMoments.Any(moment => moment is null);

    private static void ValidateUniqueIds(
        UserDataExportDocument document,
        Dictionary<string, HashSet<Guid>> ids,
        HashSet<UserDataPortabilityError> errors)
    {
        AddIds("relationshipTypes", document.RelationshipTypes.Select(x => x.Id));
        AddIds("tags", document.Tags.Select(x => x.Id));
        AddIds("people", document.People.Select(x => x.Id));
        AddIds("contacts", document.People.SelectMany(x => x.ContactMethods ?? []).Select(x => x.Id));
        AddIds("interactions", document.Interactions.Select(x => x.Id));
        AddIds("participants", document.Interactions.SelectMany(x => x.Participants ?? []).Select(x => x.Id));
        AddIds("notes", document.Notes.Select(x => x.Id));
        AddIds("reminders", document.Reminders.Select(x => x.Id));
        AddIds("difficultMoments", document.DifficultMoments.Select(x => x.Id));

        void AddIds(string kind, IEnumerable<Guid> source)
        {
            var values = source.ToArray();
            var set = values.ToHashSet();
            if (values.Any(x => x == Guid.Empty) || values.Length != set.Count)
            {
                errors.Add(UserDataPortabilityError.DuplicateId);
            }

            ids[kind] = set;
        }
    }

    private static void ValidateAudits(UserDataExportDocument document, HashSet<UserDataPortabilityError> errors)
    {
        foreach (var row in document.RelationshipTypes)
        {
            ValidateAudit(row.CreatedAtUtc, row.UpdatedAtUtc, document.ExportedAtUtc, errors);
            if (row.SortOrder < 0 || row.Name is null || row.Name.Length > RelationshipType.NameMaxLength)
            {
                errors.Add(UserDataPortabilityError.InvalidValue);
            }
        }

        if (document.RelationshipTypes.Select(x => x.SortOrder).Distinct().Count() != document.RelationshipTypes.Count)
        {
            errors.Add(UserDataPortabilityError.InvalidValue);
        }

        foreach (var row in document.Tags)
        {
            ValidateAudit(row.CreatedAtUtc, row.UpdatedAtUtc, document.ExportedAtUtc, errors);
            if (row.Name is null || row.Name.Length > Tag.NameMaxLength)
            {
                errors.Add(UserDataPortabilityError.InvalidValue);
            }
        }

        foreach (var person in document.People)
        {
            ValidateAudit(person.CreatedAtUtc, person.UpdatedAtUtc, document.ExportedAtUtc, errors);
            if (person.ArchivedAtUtc is { } archivedAt)
            {
                if (!IsUtc(archivedAt)
                    || archivedAt < person.CreatedAtUtc
                    || archivedAt > person.UpdatedAtUtc
                    || archivedAt > document.ExportedAtUtc)
                {
                    errors.Add(UserDataPortabilityError.InvalidAuditDate);
                }
            }

            foreach (var contact in person.ContactMethods ?? [])
            {
                ValidateAudit(contact.CreatedAtUtc, contact.UpdatedAtUtc, document.ExportedAtUtc, errors);
                if (contact.SortOrder < 0)
                {
                    errors.Add(UserDataPortabilityError.InvalidValue);
                }
            }
        }

        foreach (var interaction in document.Interactions)
        {
            ValidateAudit(interaction.CreatedAtUtc, interaction.UpdatedAtUtc, document.ExportedAtUtc, errors);
            foreach (var participant in interaction.Participants ?? [])
            {
                ValidateAudit(participant.CreatedAtUtc, participant.UpdatedAtUtc, document.ExportedAtUtc, errors);
            }
        }

        foreach (var note in document.Notes)
        {
            ValidateAudit(note.CreatedAtUtc, note.UpdatedAtUtc, document.ExportedAtUtc, errors);
        }

        foreach (var reminder in document.Reminders)
        {
            ValidateAudit(reminder.CreatedAtUtc, reminder.UpdatedAtUtc, document.ExportedAtUtc, errors);
            if (reminder.CompletedAtUtc is { } completedAt
                && (!IsUtc(completedAt)
                    || completedAt < reminder.CreatedAtUtc
                || completedAt > reminder.UpdatedAtUtc
                || completedAt > document.ExportedAtUtc
                    || !reminder.IsCompleted))
            {
                errors.Add(UserDataPortabilityError.InvalidAuditDate);
            }
        }

        foreach (var moment in document.DifficultMoments)
        {
            ValidateAudit(moment.CreatedAtUtc, moment.UpdatedAtUtc, document.ExportedAtUtc, errors);
        }
    }

    private static void ValidateReferencesAndValues(
        UserDataExportDocument document,
        DateTimeOffset nowUtc,
        HashSet<UserDataPortabilityError> errors)
    {
        var timeZoneValid = TimeZoneIds.TryParse(document.Profile.TimeZoneId, out var timeZone);
        var today = timeZoneValid ? UserCalendar.ToUserDate(nowUtc, timeZone) : DateOnly.FromDateTime(nowUtc.UtcDateTime);
        var relationshipIds = document.RelationshipTypes.Select(x => x.Id).ToHashSet();
        var tagIds = document.Tags.Select(x => x.Id).ToHashSet();
        var momentIds = document.DifficultMoments.Select(x => x.Id).ToHashSet();
        var people = document.People
            .GroupBy(x => x.Id)
            .Select(group => group.First())
            .ToDictionary(x => x.Id);

        foreach (var person in document.People)
        {
            if (person.RelationshipTypeId is { } relationshipId && !relationshipIds.Contains(relationshipId))
            {
                errors.Add(UserDataPortabilityError.InvalidReference);
            }

            if ((person.TagIds ?? []).Any(x => !tagIds.Contains(x)))
            {
                errors.Add(UserDataPortabilityError.InvalidReference);
            }

            if (person.ContactMethods is null
                || person.TagIds is null
                || person.ContactMethods.Count > ContactMethodRules.MaxPerPerson
                || person.TagIds.Count > TagNameRules.MaxPerPerson
                || person.TagIds.Distinct().Count() != person.TagIds.Count
                || person.LastContactedOn is { } lastContacted && lastContacted > today
                || person.StayInTouchCadenceDays is < 1
                || person.BirthdayReminderLeadDays is < 0)
            {
                errors.Add(UserDataPortabilityError.InvalidValue);
            }

            if (!person.IsArchived && person.ArchivedAtUtc is not null)
            {
                errors.Add(UserDataPortabilityError.InvalidValue);
            }

            var profileErrors = PersonProfileRules.Validate(person, today);
            var contactErrors = PersonProfileRules.ValidateContactMethods(person);
            if (profileErrors.Count > 0 || contactErrors.Count > 0)
            {
                errors.Add(UserDataPortabilityError.InvalidValue);
            }

            var contacts = person.ContactMethods ?? [];
            if (contacts.Select(x => x.SortOrder).Distinct().Count() != contacts.Count)
            {
                errors.Add(UserDataPortabilityError.InvalidValue);
            }
        }

        foreach (var interaction in document.Interactions)
        {
            var participants = interaction.Participants;
            if (participants is null
                || participants.Count is 0 or > InteractionRules.MaxParticipants
                || participants.Select(x => x.PersonId).Distinct().Count() != participants.Count
                || participants.Any(x => !people.ContainsKey(x.PersonId)))
            {
                errors.Add(UserDataPortabilityError.InvalidReference);
                continue;
            }

            var validation = InteractionRules.Validate(
                interaction.OccurredOn,
                interaction.Kind,
                interaction.Description,
                participants.Select(x => x.PersonId).ToArray(),
                today);
            if (validation.Count > 0)
            {
                errors.Add(UserDataPortabilityError.InvalidValue);
            }
        }

        foreach (var note in document.Notes)
        {
            if (!people.ContainsKey(note.PersonId))
            {
                errors.Add(UserDataPortabilityError.InvalidReference);
            }

            if (NoteRules.Validate(note.Text).Count > 0)
            {
                errors.Add(UserDataPortabilityError.InvalidValue);
            }
        }

        foreach (var reminder in document.Reminders)
        {
            if (!people.ContainsKey(reminder.PersonId))
            {
                errors.Add(UserDataPortabilityError.InvalidReference);
            }

            var validation = ReminderRules.Validate(new CreateReminderRequest
            {
                PersonId = reminder.PersonId,
                Title = reminder.Title,
                DueDate = reminder.DueDate,
                Frequency = reminder.Frequency,
                CustomIntervalMonths = reminder.CustomIntervalMonths,
            });
            if (reminder.DueDate == default
                || validation.Count > 0
                || reminder.IsCompleted != (reminder.CompletedAtUtc is not null)
                || reminder.LastDeliveredDate is { } delivered
                    && delivered > today)
            {
                errors.Add(UserDataPortabilityError.InvalidValue);
            }
        }

        foreach (var moment in document.DifficultMoments)
        {
            if (!people.ContainsKey(moment.PersonId))
            {
                errors.Add(UserDataPortabilityError.InvalidReference);
            }

            if (moment.RecurrenceOfId.HasValue
                && (!momentIds.Contains(moment.RecurrenceOfId.Value) || moment.RecurrenceOfId.Value == moment.Id))
            {
                errors.Add(UserDataPortabilityError.InvalidReference);
            }

            var validation = DifficultMomentRules.Validate(
                moment.OccurredOn,
                moment.Description,
                moment.Trigger,
                moment.Resolution,
                moment.LessonsLearned,
                moment.Status,
                moment.ResolvedOn,
                moment.RecurrenceOfId,
                moment.Id,
                today);

            if (validation.Count > 0)
            {
                errors.Add(UserDataPortabilityError.InvalidValue);
            }
        }
    }

    private static void ValidateProductActivity(
        ProductActivitySnapshot? activity,
        DateTime exportedAtUtc,
        DateTimeOffset nowUtc,
        HashSet<UserDataPortabilityError> errors)
    {
        if (activity is null)
        {
            return;
        }

        var todayUtc = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        DateTime? expectedExpiry = null;
        if (activity.CohortStartedOnUtc <= DateOnly.MaxValue.AddDays(-ProductActivityCohortRules.RetentionDays))
        {
            expectedExpiry = ProductActivityCohortRules.RetentionExpiresAtUtc(activity.CohortStartedOnUtc);
        }
        var returnedWindow = ProductActivityCohortRules.IsWithinReturnWindow(
            activity.CohortStartedOnUtc,
            activity.LastActiveOnUtc);
        var lastActiveAge = activity.LastActiveOnUtc.DayNumber - activity.CohortStartedOnUtc.DayNumber;

        if (!IsUtc(activity.RetentionExpiresAtUtc)
            || activity.Id == Guid.Empty
            || activity.CohortStartedOnUtc == default
            || activity.LastActiveOnUtc < activity.CohortStartedOnUtc
            || activity.LastActiveOnUtc > todayUtc
            || activity.CohortStartedOnUtc > todayUtc
            || expectedExpiry is null
            || activity.RetentionExpiresAtUtc != expectedExpiry.Value
            || returnedWindow && !activity.ReturnedInDays30To59
            || activity.ReturnedInDays30To59
                && lastActiveAge < ProductActivityCohortRules.ReturnWindowStartDay)
        {
            errors.Add(UserDataPortabilityError.InvalidValue);
        }

        ValidateAudit(activity.CreatedAtUtc, activity.UpdatedAtUtc, exportedAtUtc, errors);
    }

    private static void ValidateUniqueNames(UserDataExportDocument document, HashSet<UserDataPortabilityError> errors)
    {
        ValidateNames(document.RelationshipTypes.Select(x => x.Name), RelationshipType.NameMaxLength, errors);
        ValidateNames(document.Tags.Select(x => x.Name), Tag.NameMaxLength, errors);
    }

    private static void ValidateNames(
        IEnumerable<string?> names,
        int maxLength,
        HashSet<UserDataPortabilityError> errors)
    {
        var normalized = names.Select(LabelNameRules.Normalize).ToArray();
        if (normalized.Any(x => x is null)
            || normalized.OfType<string>().Any(x =>
                LabelNameRules.Validate(x, maxLength) is not null)
            || normalized.OfType<string>().Distinct(TagNameRules.Comparer).Count() != normalized.Length)
        {
            errors.Add(UserDataPortabilityError.InvalidValue);
        }
    }

    private static void ValidateAudit(
        DateTime createdAtUtc,
        DateTime updatedAtUtc,
        DateTime documentDateUtc,
        HashSet<UserDataPortabilityError> errors)
    {
        if (!IsUtc(createdAtUtc)
            || !IsUtc(updatedAtUtc)
            || createdAtUtc > updatedAtUtc
            || updatedAtUtc > documentDateUtc)
        {
            errors.Add(UserDataPortabilityError.InvalidAuditDate);
        }
    }

    private static bool IsUtc(DateTime value) => value.Kind == DateTimeKind.Utc;

    private static long TotalRows(UserDataExportDocument document) =>
        1L
        + document.RelationshipTypes.Count
        + document.Tags.Count
        + document.People.Count
        + document.People.Sum(x => (long)(x?.ContactMethods?.Count ?? 0))
        + document.Interactions.Count
        + document.Interactions.Sum(x => (long)(x?.Participants?.Count ?? 0))
        + document.Notes.Count
        + document.Reminders.Count
        + document.DifficultMoments.Count
        + (document.ProductActivity is null ? 0 : 1);
}
