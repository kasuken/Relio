using Microsoft.EntityFrameworkCore;
using Relio.Application.Interactions;
using Relio.Application.Notes;
using Relio.Application.People;
using Relio.Application.Portability;
using Relio.Application.Reminders;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Data.People;
using Relio.Data.Reminders;
using Relio.Domain;

namespace Relio.Data.Portability;

/// <summary>EF-backed export and fresh-account restore for the current user's data.</summary>
public sealed class UserDataPortabilityService(
    RelioDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IUserDataPortabilityService
{
    /// <inheritdoc />
    public async Task<UserDataExportDocument> ExportAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var profile = await dbContext.UserProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == ownerId, cancellationToken);
        if (profile is null)
        {
            throw new UserDataPortabilityException([UserDataPortabilityError.InvalidDocument]);
        }

        var relationshipTypes = await dbContext.RelationshipTypes
            .AsNoTracking()
            .Where(item => item.OwnerId == ownerId)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

        var tags = await dbContext.Tags
            .AsNoTracking()
            .Where(item => item.OwnerId == ownerId)
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

        var people = await dbContext.People
            .AsNoTracking()
            .Where(item => item.OwnerId == ownerId)
            .Include(item => item.Tags.Where(tag => tag.OwnerId == ownerId))
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        var personIds = people.Select(item => item.Id).ToArray();

        var contacts = personIds.Length == 0
            ? new List<ContactMethod>()
            : await dbContext.ContactMethods
                .AsNoTracking()
                .Where(item => item.OwnerId == ownerId && personIds.Contains(item.PersonId))
                .OrderBy(item => item.PersonId)
                .ThenBy(item => item.SortOrder)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

        var tagLinks = people
            .SelectMany(person => person.Tags.Select(tag => new PersonTagLink(person.Id, tag.Id)))
            .ToArray();

        var interactions = await dbContext.Interactions
            .AsNoTracking()
            .Where(item => item.OwnerId == ownerId)
            .OrderBy(item => item.OccurredOn)
            .ThenBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        var interactionIds = interactions.Select(item => item.Id).ToArray();

        var participants = interactionIds.Length == 0 || personIds.Length == 0
            ? new List<InteractionParticipant>()
            : await dbContext.InteractionParticipants
                .AsNoTracking()
                .Where(item => item.OwnerId == ownerId
                    && interactionIds.Contains(item.InteractionId)
                    && personIds.Contains(item.PersonId))
                .OrderBy(item => item.InteractionId)
                .ThenBy(item => item.PersonId)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

        var notes = personIds.Length == 0
            ? new List<Note>()
            : await dbContext.Notes
                .AsNoTracking()
                .Where(item => item.OwnerId == ownerId && personIds.Contains(item.PersonId))
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

        var reminders = personIds.Length == 0
            ? new List<Reminder>()
            : await dbContext.Reminders
                .AsNoTracking()
                .Where(item => item.OwnerId == ownerId && personIds.Contains(item.PersonId))
                .OrderBy(item => item.DueDate)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

        var productActivity = await dbContext.Set<ProductActivity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == ownerId, cancellationToken);

        var contactsByPerson = contacts
            .GroupBy(item => item.PersonId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ContactMethodSnapshot>)group.Select(ToSnapshot).ToArray());
        var tagsByPerson = tagLinks
            .GroupBy(link => link.PersonId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Guid>)group.Select(link => link.TagId).Order().ToArray());
        var participantsByInteraction = participants
            .GroupBy(item => item.InteractionId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<InteractionParticipantSnapshot>)group.Select(ToSnapshot).ToArray());

        var exportedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        return new UserDataExportDocument
        {
            FormatVersion = UserDataExportDocument.CurrentFormatVersion,
            ExportedAtUtc = exportedAtUtc,
            Profile = new UserProfileSnapshot
            {
                Id = profile.Id,
                CreatedAtUtc = AsUtc(profile.CreatedAtUtc),
                UpdatedAtUtc = AsUtc(profile.UpdatedAtUtc),
                TimeZoneId = profile.TimeZoneId,
                DisplayName = profile.DisplayName,
                OnboardingDismissed = profile.OnboardingDismissed,
                BirthdayRemindersEnabled = profile.BirthdayRemindersEnabled,
                DefaultBirthdayLeadDays = profile.DefaultBirthdayLeadDays,
                ReminderEmailDelivery = profile.ReminderEmailDelivery,
            },
            RelationshipTypes = relationshipTypes.Select(item => new RelationshipTypeSnapshot
            {
                Id = item.Id,
                Name = item.Name,
                SortOrder = item.SortOrder,
                CreatedAtUtc = AsUtc(item.CreatedAtUtc),
                UpdatedAtUtc = AsUtc(item.UpdatedAtUtc),
            }).ToArray(),
            Tags = tags.Select(item => new TagSnapshot
            {
                Id = item.Id,
                Name = item.Name,
                CreatedAtUtc = AsUtc(item.CreatedAtUtc),
                UpdatedAtUtc = AsUtc(item.UpdatedAtUtc),
            }).ToArray(),
            People = people.Select(item => new PersonSnapshot
            {
                Id = item.Id,
                CreatedAtUtc = AsUtc(item.CreatedAtUtc),
                UpdatedAtUtc = AsUtc(item.UpdatedAtUtc),
                FirstName = item.FirstName,
                LastName = item.LastName,
                Nickname = item.Nickname,
                RelationshipTypeId = item.RelationshipTypeId,
                BirthdayDay = item.BirthdayDay,
                BirthdayMonth = item.BirthdayMonth,
                BirthdayYear = item.BirthdayYear,
                HowWeMet = item.HowWeMet,
                Details = item.Details,
                IsArchived = item.IsArchived,
                ArchivedAtUtc = item.ArchivedAtUtc is { } archived ? AsUtc(archived) : null,
                LastContactedOn = item.LastContactedOn,
                StayInTouchCadenceDays = item.StayInTouchCadenceDays,
                BirthdayReminderDisabled = item.BirthdayReminderDisabled,
                BirthdayReminderLeadDays = item.BirthdayReminderLeadDays,
                ContactMethods = contactsByPerson.GetValueOrDefault(item.Id) ?? [],
                TagIds = tagsByPerson.GetValueOrDefault(item.Id) ?? [],
            }).ToArray(),
            Interactions = interactions.Select(item => new InteractionSnapshot
            {
                Id = item.Id,
                CreatedAtUtc = AsUtc(item.CreatedAtUtc),
                UpdatedAtUtc = AsUtc(item.UpdatedAtUtc),
                OccurredOn = item.OccurredOn,
                Kind = item.Kind,
                Description = item.Description,
                Participants = participantsByInteraction.GetValueOrDefault(item.Id) ?? [],
            }).ToArray(),
            Notes = notes.Select(item => new NoteSnapshot
            {
                Id = item.Id,
                PersonId = item.PersonId,
                Text = item.Text,
                IsPinned = item.IsPinned,
                CreatedAtUtc = AsUtc(item.CreatedAtUtc),
                UpdatedAtUtc = AsUtc(item.UpdatedAtUtc),
            }).ToArray(),
            Reminders = reminders.Select(item => new ReminderSnapshot
            {
                Id = item.Id,
                PersonId = item.PersonId,
                Title = item.Title,
                DueDate = item.DueDate,
                Frequency = item.Frequency,
                CustomIntervalMonths = item.CustomIntervalMonths,
                SnoozedUntilDate = item.SnoozedUntilDate,
                IsCompleted = item.IsCompleted,
                CompletedAtUtc = item.CompletedAtUtc is { } completed ? AsUtc(completed) : null,
                LastDeliveredDate = item.LastDeliveredDate,
                CreatedAtUtc = AsUtc(item.CreatedAtUtc),
                UpdatedAtUtc = AsUtc(item.UpdatedAtUtc),
            }).ToArray(),
            ProductActivity = productActivity is null
                ? null
                : new ProductActivitySnapshot
                {
                    Id = productActivity.Id,
                    CreatedAtUtc = AsUtc(productActivity.CreatedAtUtc),
                    UpdatedAtUtc = AsUtc(productActivity.UpdatedAtUtc),
                    CohortStartedOnUtc = productActivity.CohortStartedOnUtc,
                    LastActiveOnUtc = productActivity.LastActiveOnUtc,
                    ReturnedInDays30To59 = productActivity.ReturnedInDays30To59,
                    RetentionExpiresAtUtc = AsUtc(productActivity.RetentionExpiresAtUtc),
                },
        };
    }

    /// <inheritdoc />
    public async Task<string> ExportPeopleVCardAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var people = await dbContext.People
            .AsNoTracking()
            .Where(item => item.OwnerId == ownerId)
            .OrderBy(item => item.FirstName)
            .ThenBy(item => item.LastName)
            .ThenBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.FirstName,
                item.LastName,
                item.Nickname,
                item.BirthdayDay,
                item.BirthdayMonth,
                item.BirthdayYear,
            })
            .ToListAsync(cancellationToken);
        var personIds = people.Select(item => item.Id).ToArray();

        var contacts = personIds.Length == 0
            ? new List<VCardContactMethodRow>()
            : await dbContext.ContactMethods
                .AsNoTracking()
                .Where(item => item.OwnerId == ownerId && personIds.Contains(item.PersonId))
                .OrderBy(item => item.PersonId)
                .ThenBy(item => item.SortOrder)
                .ThenBy(item => item.Id)
                .Select(item => new VCardContactMethodRow(
                    item.PersonId,
                    item.Kind,
                    item.Label,
                    item.Value,
                    item.SortOrder))
                .ToListAsync(cancellationToken);
        var contactsByPerson = contacts
            .GroupBy(item => item.PersonId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<VCardContactMethod>)group.Select(item =>
                    new VCardContactMethod(item.Kind, item.Label, item.Value, item.SortOrder)).ToArray());

        var vCardPeople = people.Select(item => new VCardPerson
        {
            FirstName = item.FirstName,
            LastName = item.LastName,
            Nickname = item.Nickname,
            BirthdayDay = item.BirthdayDay,
            BirthdayMonth = item.BirthdayMonth,
            BirthdayYear = item.BirthdayYear,
            ContactMethods = contactsByPerson.GetValueOrDefault(item.Id) ?? [],
        }).ToArray();
        return PeopleVCardWriter.Write(vCardPeople);
    }

    /// <inheritdoc />
    public async Task<UserDataRestoreResult> RestoreAsync(
        UserDataExportDocument document,
        CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        try
        {
            var nowUtc = timeProvider.GetUtcNow();
            var validationErrors = UserDataPortabilityRules.Validate(document, nowUtc);
            if (validationErrors.Count > 0)
            {
                throw new UserDataPortabilityException(validationErrors);
            }

            if (!dbContext.Database.IsSqlServer() && !dbContext.Database.IsInMemory())
            {
                throw new InvalidOperationException(
                    "Data restore concurrency protection supports SQL Server and the InMemory test provider.");
            }

            await using var inMemoryRestoreLock = await UserDataPortabilityInMemoryRestoreLock.EnterAsync(
                dbContext,
                ownerId,
                cancellationToken);

            var profileExists = await dbContext.People.AsNoTracking()
                .AnyAsync(item => item.OwnerId == ownerId, cancellationToken);
            if (!profileExists)
            {
                profileExists = await dbContext.ContactMethods.AsNoTracking()
                    .AnyAsync(item => item.OwnerId == ownerId, cancellationToken);
            }

            if (!profileExists)
            {
                profileExists = await dbContext.Tags.AsNoTracking()
                    .AnyAsync(item => item.OwnerId == ownerId, cancellationToken);
            }

            if (!profileExists)
            {
                profileExists = await dbContext.Interactions.AsNoTracking()
                    .AnyAsync(item => item.OwnerId == ownerId, cancellationToken);
            }

            if (!profileExists)
            {
                profileExists = await dbContext.InteractionParticipants.AsNoTracking()
                    .AnyAsync(item => item.OwnerId == ownerId, cancellationToken);
            }

            if (!profileExists)
            {
                profileExists = await dbContext.Notes.AsNoTracking()
                    .AnyAsync(item => item.OwnerId == ownerId, cancellationToken);
            }

            if (!profileExists)
            {
                profileExists = await dbContext.Reminders.AsNoTracking()
                    .AnyAsync(item => item.OwnerId == ownerId, cancellationToken);
            }

            if (profileExists)
            {
                throw new UserDataPortabilityException([UserDataPortabilityError.DestinationNotFresh]);
            }

            var profile = await dbContext.UserProfiles
                .SingleOrDefaultAsync(item => item.OwnerId == ownerId, cancellationToken);
            var currentTypes = await dbContext.RelationshipTypes
                .Where(item => item.OwnerId == ownerId)
                .ToListAsync(cancellationToken);
            var defaultNames = RelationshipType.DefaultNames
                .Select(LabelNameRules.Normalize)
                .OfType<string>()
                .ToHashSet(LabelNameRules.Comparer);
            var currentTypeNames = currentTypes
                .Select(type => LabelNameRules.Normalize(type.Name))
                .ToArray();
            if (currentTypeNames.Any(name => name is null || !defaultNames.Contains(name))
                || currentTypeNames.OfType<string>().Distinct(LabelNameRules.Comparer).Count() != currentTypes.Count)
            {
                throw new UserDataPortabilityException([UserDataPortabilityError.DestinationNotFresh]);
            }

            var auditMappings = new List<ImportedAuditMapping>();
            void PreserveAudit(IOwnedEntity entity, DateTime createdAtUtc, DateTime updatedAtUtc) =>
                auditMappings.Add(new ImportedAuditMapping(entity, createdAtUtc, updatedAtUtc));

            var usedCurrentTypes = new HashSet<Guid>();
            var relationshipTypeMap = new Dictionary<Guid, RelationshipType>();
            var currentTypesByName = currentTypes.ToDictionary(
                item => LabelNameRules.Normalize(item.Name)!,
                LabelNameRules.Comparer);
            var newTypes = new List<RelationshipType>();
            foreach (var sourceType in document.RelationshipTypes)
            {
                var name = LabelNameRules.Normalize(sourceType.Name)!;
                if (currentTypesByName.TryGetValue(name, out var existingType))
                {
                    existingType.Name = name;
                    existingType.SortOrder = sourceType.SortOrder;
                    dbContext.Entry(existingType).State = EntityState.Modified;
                    usedCurrentTypes.Add(existingType.Id);
                    relationshipTypeMap.Add(sourceType.Id, existingType);
                    PreserveAudit(existingType, sourceType.CreatedAtUtc, sourceType.UpdatedAtUtc);
                }
                else
                {
                    var newType = new RelationshipType
                    {
                        OwnerId = ownerId,
                        Name = name,
                        SortOrder = sourceType.SortOrder,
                    };
                    newTypes.Add(newType);
                    relationshipTypeMap.Add(sourceType.Id, newType);
                    PreserveAudit(newType, sourceType.CreatedAtUtc, sourceType.UpdatedAtUtc);
                }
            }

            dbContext.RelationshipTypes.RemoveRange(currentTypes.Where(item => !usedCurrentTypes.Contains(item.Id)));
            dbContext.RelationshipTypes.AddRange(newTypes);

            var tagsBySourceId = new Dictionary<Guid, Tag>();
            var newTags = new List<Tag>();
            foreach (var sourceTag in document.Tags)
            {
                var name = TagNameRules.Normalize(sourceTag.Name)!;
                var tag = new Tag { OwnerId = ownerId, Name = name };
                newTags.Add(tag);
                tagsBySourceId.Add(sourceTag.Id, tag);
                PreserveAudit(tag, sourceTag.CreatedAtUtc, sourceTag.UpdatedAtUtc);
            }

            dbContext.Tags.AddRange(newTags);

            profile ??= new UserProfile { OwnerId = ownerId };
            profile.TimeZoneId = TimeZoneIds.Parse(document.Profile.TimeZoneId).Id;
            profile.DisplayName = PersonProfileRules.NormalizeOptional(document.Profile.DisplayName);
            profile.OnboardingDismissed = document.Profile.OnboardingDismissed;
            profile.BirthdayRemindersEnabled = document.Profile.BirthdayRemindersEnabled;
            profile.DefaultBirthdayLeadDays = document.Profile.DefaultBirthdayLeadDays;
            profile.ReminderEmailDelivery = ReminderEmailDelivery.None;
            profile.UnsubscribeToken = UnsubscribeTokens.Generate();
            profile.UnsubscribeTokenVerifier = null;
            if (dbContext.Entry(profile).State == EntityState.Detached)
            {
                dbContext.UserProfiles.Add(profile);
            }

            PreserveAudit(profile, document.Profile.CreatedAtUtc, document.Profile.UpdatedAtUtc);

            var peopleBySourceId = new Dictionary<Guid, Person>();
            foreach (var sourcePerson in document.People)
            {
                var relationshipTypeId = sourcePerson.RelationshipTypeId is { } sourceTypeId
                    ? relationshipTypeMap[sourceTypeId].Id
                    : (Guid?)null;
                var sourceContacts = sourcePerson.ContactMethods
                    .OrderBy(item => item.SortOrder)
                    .ThenBy(item => item.Id)
                    .ToArray();
                var input = new RestoredPersonInput(
                    sourcePerson,
                    relationshipTypeId,
                    sourceContacts.Select(item => new ContactMethodInput(null, item.Kind, item.Label, item.Value)).ToArray(),
                    sourcePerson.TagIds.Select(id => tagsBySourceId[id].Id).ToArray());
                var person = PersonEntityBuilder.NewPerson(ownerId, input);
                person.IsArchived = sourcePerson.IsArchived;
                person.ArchivedAtUtc = sourcePerson.ArchivedAtUtc;
                person.LastContactedOn = sourcePerson.LastContactedOn;
                foreach (var tagId in sourcePerson.TagIds)
                {
                    person.Tags.Add(tagsBySourceId[tagId]);
                }

                dbContext.People.Add(person);
                peopleBySourceId.Add(sourcePerson.Id, person);
                PreserveAudit(person, sourcePerson.CreatedAtUtc, sourcePerson.UpdatedAtUtc);

                var builtContacts = person.ContactMethods.OrderBy(item => item.SortOrder).ToArray();
                for (var index = 0; index < builtContacts.Length; index++)
                {
                    builtContacts[index].SortOrder = sourceContacts[index].SortOrder;
                    PreserveAudit(builtContacts[index], sourceContacts[index].CreatedAtUtc, sourceContacts[index].UpdatedAtUtc);
                }
            }

            foreach (var sourceInteraction in document.Interactions)
            {
                var interaction = new Interaction
                {
                    OwnerId = ownerId,
                    OccurredOn = sourceInteraction.OccurredOn,
                    Kind = sourceInteraction.Kind,
                    Description = InteractionRules.NormalizeDescription(sourceInteraction.Description),
                };
                dbContext.Interactions.Add(interaction);
                PreserveAudit(interaction, sourceInteraction.CreatedAtUtc, sourceInteraction.UpdatedAtUtc);

                foreach (var sourceParticipant in sourceInteraction.Participants)
                {
                    var participant = new InteractionParticipant
                    {
                        OwnerId = ownerId,
                        InteractionId = interaction.Id,
                        PersonId = peopleBySourceId[sourceParticipant.PersonId].Id,
                    };
                    dbContext.InteractionParticipants.Add(participant);
                    PreserveAudit(participant, sourceParticipant.CreatedAtUtc, sourceParticipant.UpdatedAtUtc);
                }
            }

            foreach (var sourceNote in document.Notes)
            {
                var note = new Note
                {
                    OwnerId = ownerId,
                    PersonId = peopleBySourceId[sourceNote.PersonId].Id,
                    Text = NoteRules.NormalizeText(sourceNote.Text),
                    IsPinned = sourceNote.IsPinned,
                };
                dbContext.Notes.Add(note);
                PreserveAudit(note, sourceNote.CreatedAtUtc, sourceNote.UpdatedAtUtc);
            }

            foreach (var sourceReminder in document.Reminders)
            {
                var reminder = new Reminder
                {
                    OwnerId = ownerId,
                    PersonId = peopleBySourceId[sourceReminder.PersonId].Id,
                    Title = ReminderRules.NormalizeTitle(sourceReminder.Title),
                    DueDate = sourceReminder.DueDate,
                    Frequency = sourceReminder.Frequency,
                    CustomIntervalMonths = sourceReminder.CustomIntervalMonths,
                    SnoozedUntilDate = sourceReminder.SnoozedUntilDate,
                    IsCompleted = sourceReminder.IsCompleted,
                    CompletedAtUtc = sourceReminder.CompletedAtUtc,
                    LastDeliveredDate = sourceReminder.LastDeliveredDate,
                };
                dbContext.Reminders.Add(reminder);
                PreserveAudit(reminder, sourceReminder.CreatedAtUtc, sourceReminder.UpdatedAtUtc);
            }

            try
            {
                if (dbContext.Database.IsSqlServer())
                {
                    var originalTransactionBehavior = dbContext.Database.AutoTransactionBehavior;
                    try
                    {
                        dbContext.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
                        using (UserDataPortabilityRestoreCheck.Arm(dbContext, ownerId))
                        using (dbContext.BeginImportedAuditScope(auditMappings))
                        {
                            try
                            {
                                await dbContext.SaveChangesAsync(cancellationToken);
                            }
                            catch (DbUpdateException exception)
                                when (exception.InnerException is UserDataPortabilityException portabilityException
                                    && portabilityException.Errors.Count == 1
                                    && portabilityException.Errors[0] == UserDataPortabilityError.DestinationNotFresh)
                            {
                                throw new UserDataPortabilityException(portabilityException.Errors);
                            }
                        }
                    }
                    finally
                    {
                        dbContext.Database.AutoTransactionBehavior = originalTransactionBehavior;
                    }
                }
                else
                {
                    using (dbContext.BeginImportedAuditScope(auditMappings))
                    {
                        await dbContext.SaveChangesAsync(cancellationToken);
                    }
                }
            }
            finally
            {
                // SaveChangesAsync has completed either its SavedChanges or SaveChangesFailed callbacks.
                await inMemoryRestoreLock.DisposeAsync();
            }

            return new UserDataRestoreResult(
                document.People.Count,
                document.Interactions.Count,
                document.Notes.Count,
                document.Reminders.Count,
                document.Tags.Count,
                document.RelationshipTypes.Count);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    private static ContactMethodSnapshot ToSnapshot(ContactMethod item) => new()
    {
        Id = item.Id,
        Kind = item.Kind,
        Label = item.Label,
        Value = item.Value,
        SortOrder = item.SortOrder,
        CreatedAtUtc = AsUtc(item.CreatedAtUtc),
        UpdatedAtUtc = AsUtc(item.UpdatedAtUtc),
    };

    private static InteractionParticipantSnapshot ToSnapshot(InteractionParticipant item) => new()
    {
        Id = item.Id,
        PersonId = item.PersonId,
        CreatedAtUtc = AsUtc(item.CreatedAtUtc),
        UpdatedAtUtc = AsUtc(item.UpdatedAtUtc),
    };

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record PersonTagLink(Guid PersonId, Guid TagId);

    private sealed record VCardContactMethodRow(
        Guid PersonId,
        ContactMethodKind Kind,
        string? Label,
        string Value,
        int SortOrder);

    private sealed class RestoredPersonInput(
        PersonSnapshot source,
        Guid? relationshipTypeId,
        IReadOnlyList<ContactMethodInput> contacts,
        IReadOnlyCollection<Guid> tagIds) : IPersonProfileInput
    {
        public string FirstName => source.FirstName;
        public string? LastName => source.LastName;
        public string? Nickname => source.Nickname;
        public Guid? RelationshipTypeId => relationshipTypeId;
        public int? BirthdayDay => source.BirthdayDay;
        public int? BirthdayMonth => source.BirthdayMonth;
        public int? BirthdayYear => source.BirthdayYear;
        public string? HowWeMet => source.HowWeMet;
        public string? Details => source.Details;
        public IReadOnlyList<ContactMethodInput>? ContactMethods => contacts;
        public IReadOnlyCollection<Guid>? TagIds => tagIds;
        public IReadOnlyCollection<string>? NewTagNames => [];
        public int? StayInTouchCadenceDays => source.StayInTouchCadenceDays;
        public bool BirthdayReminderDisabled => source.BirthdayReminderDisabled;
        public int? BirthdayReminderLeadDays => source.BirthdayReminderLeadDays;
    }
}
