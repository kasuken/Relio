using Relio.Application.Paging;
using Relio.Application.People;
using Relio.Domain;

namespace Relio.Web.Tests.People;

/// <summary>
/// In-memory <see cref="IPeopleService"/> for component tests: serves the people a test puts in
/// <see cref="Known"/>, records what was created, and can be told to throw once on the next create.
/// </summary>
internal sealed class FakePeopleService : IPeopleService
{
    public List<Person> Known { get; } = [];

    public List<CreatePersonRequest> Created { get; } = [];

    /// <summary>The id the next created person gets.</summary>
    public Guid NextId { get; set; } = Guid.NewGuid();

    /// <summary>Thrown by (and then cleared from) the next <see cref="CreateAsync"/> call.</summary>
    public Exception? ThrowOnNextCreate { get; set; }

    public Task<Person?> GetAsync(Guid personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Known.FirstOrDefault(p => p.Id == personId));

    public Task<IReadOnlyList<Person>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Person>>(Known.Where(p => includeArchived || !p.IsArchived).ToList());

    /// <summary>Every query <see cref="ListPageAsync"/> was called with, in order.</summary>
    public List<PeopleListQuery> ListPageQueries { get; } = [];

    /// <summary>What <see cref="ListPageAsync"/> returns; when unset, a page built from <see cref="Known"/>.</summary>
    public Func<PeopleListQuery, PeopleListResult>? ListPageResult { get; set; }

    public Task<PeopleListResult> ListPageAsync(PeopleListQuery query, CancellationToken cancellationToken = default)
    {
        ListPageQueries.Add(query);
        if (ListPageResult is { } result)
        {
            return Task.FromResult(result(query));
        }

        var items = Known
            .Where(p => query.IncludeArchived || !p.IsArchived)
            .Select(p => new PersonListItem(
                p.Id, p.FirstName, p.LastName, p.RelationshipType?.Name, p.LastContactedOn, p.IsArchived, p.CreatedAtUtc))
            .ToList();
        var page = new PagedResult<PersonListItem>(items, 1, query.PageSize, items.Count);
        return Task.FromResult(new PeopleListResult(
            page, Known.Count(p => !p.IsArchived), Known.Count(p => p.IsArchived)));
    }

    /// <summary>Every query <see cref="FindPossibleDuplicatesAsync"/> was called with, in order.</summary>
    public List<PossibleDuplicateQuery> DuplicateQueries { get; } = [];

    /// <summary>What <see cref="FindPossibleDuplicatesAsync"/> returns; when unset, no matches.</summary>
    public Func<PossibleDuplicateQuery, IReadOnlyList<PossibleDuplicate>>? Duplicates { get; set; }

    public Task<IReadOnlyList<PossibleDuplicate>> FindPossibleDuplicatesAsync(PossibleDuplicateQuery query, CancellationToken cancellationToken = default)
    {
        DuplicateQueries.Add(query);
        return Task.FromResult(Duplicates?.Invoke(query) ?? []);
    }

    public Task<Person> CreateAsync(CreatePersonRequest request, CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextCreate is { } exception)
        {
            ThrowOnNextCreate = null;
            throw exception;
        }

        Created.Add(request);
        var person = new Person { Id = NextId, FirstName = request.FirstName.Trim() };
        Known.Add(person);
        return Task.FromResult(person);
    }

    /// <summary>Every update, with the id it was for, in order.</summary>
    public List<(Guid PersonId, UpdatePersonRequest Request)> Updated { get; } = [];

    /// <summary>What <see cref="UpdateAsync"/> returns: <see langword="false"/> means the person is gone.</summary>
    public bool UpdateResult { get; set; } = true;

    /// <summary>Thrown by (and then cleared from) the next <see cref="UpdateAsync"/> call.</summary>
    public Exception? ThrowOnNextUpdate { get; set; }

    public Task<bool> UpdateAsync(Guid personId, UpdatePersonRequest request, CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextUpdate is { } exception)
        {
            ThrowOnNextUpdate = null;
            throw exception;
        }

        Updated.Add((personId, request));
        return Task.FromResult(UpdateResult);
    }

    /// <summary>The ids <see cref="ArchiveAsync"/>, <see cref="RestoreAsync"/> and <see cref="DeleteAsync"/> were called with, in order.</summary>
    public List<Guid> Archived { get; } = [];

    public List<Guid> Restored { get; } = [];

    public List<Guid> Deleted { get; } = [];

    /// <summary>What each of them returns; <see langword="false"/> means the person is gone. On success they change <see cref="Known"/> like the real service.</summary>
    public bool ArchiveResult { get; set; } = true;

    public bool RestoreResult { get; set; } = true;

    public bool DeleteResult { get; set; } = true;

    /// <summary>The time an archive stamps on the person.</summary>
    public DateTime ArchiveTimeUtc { get; set; } = new(2026, 10, 6, 11, 30, 0, DateTimeKind.Utc);

    public Task<bool> ArchiveAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        Archived.Add(personId);
        if (ArchiveResult && Known.FirstOrDefault(p => p.Id == personId) is { } person && !person.IsArchived)
        {
            person.IsArchived = true;
            person.ArchivedAtUtc = ArchiveTimeUtc;
        }

        return Task.FromResult(ArchiveResult);
    }

    public Task<bool> RestoreAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        Restored.Add(personId);
        if (RestoreResult && Known.FirstOrDefault(p => p.Id == personId) is { } person)
        {
            person.IsArchived = false;
            person.ArchivedAtUtc = null;
        }

        return Task.FromResult(RestoreResult);
    }

    public Task<bool> DeleteAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        Deleted.Add(personId);
        if (DeleteResult)
        {
            Known.RemoveAll(p => p.Id == personId);
        }

        return Task.FromResult(DeleteResult);
    }
}

/// <summary>
/// An in-memory <see cref="IRelationshipTypeService"/> for component tests: lists whatever a test
/// puts in <see cref="Types"/> (with the people counts in <see cref="PeopleCounts"/>), applies the
/// real name rules, records every mutation, and can be told to report "gone" or to throw.
/// </summary>
internal sealed class FakeRelationshipTypeService : IRelationshipTypeService
{
    public List<RelationshipType> Types { get; } = [];

    /// <summary>How many people have each type (by id); a type not in here has none.</summary>
    public Dictionary<Guid, int> PeopleCounts { get; } = [];

    public int ListCalls { get; private set; }

    public int ListWithUsageCalls { get; private set; }

    public List<string?> Created { get; } = [];

    public List<(Guid Id, string? Name)> Renamed { get; } = [];

    public List<(Guid Id, Guid? ReassignToId)> Deleted { get; } = [];

    /// <summary>What <see cref="RenameAsync"/> returns when the rules accept the name: <see langword="false"/> means the type is gone.</summary>
    public bool RenameResult { get; set; } = true;

    /// <summary>What <see cref="DeleteAsync"/> returns: <see langword="false"/> means the type is gone.</summary>
    public bool DeleteResult { get; set; } = true;

    /// <summary>Thrown by (and then cleared from) the next <see cref="DeleteAsync"/> call.</summary>
    public Exception? ThrowOnNextDelete { get; set; }

    /// <summary>Adds a type to the list, with the number of people who have it.</summary>
    public RelationshipType Add(string name, int people = 0)
    {
        var type = new RelationshipType { Id = Guid.NewGuid(), OwnerId = "owner", Name = name, SortOrder = Types.Count };
        Types.Add(type);
        PeopleCounts[type.Id] = people;
        return type;
    }

    public Task<IReadOnlyList<RelationshipType>> ListAsync(CancellationToken cancellationToken = default)
    {
        ListCalls++;
        return Task.FromResult<IReadOnlyList<RelationshipType>>(Types.ToList());
    }

    public Task<IReadOnlyList<RelationshipTypeUsage>> ListWithUsageAsync(CancellationToken cancellationToken = default)
    {
        ListWithUsageCalls++;
        return Task.FromResult<IReadOnlyList<RelationshipTypeUsage>>(Types
            .OrderBy(t => t.SortOrder)
            .Select(t => new RelationshipTypeUsage(t.Id, t.Name, t.SortOrder, PeopleCounts.GetValueOrDefault(t.Id)))
            .ToList());
    }

    public Task<RelationshipType> CreateAsync(string? name, CancellationToken cancellationToken = default)
    {
        Created.Add(name);
        var normalized = Validate(name, null);
        return Task.FromResult(Add(normalized));
    }

    public Task<bool> RenameAsync(Guid relationshipTypeId, string? name, CancellationToken cancellationToken = default)
    {
        Renamed.Add((relationshipTypeId, name));
        var normalized = Validate(name, relationshipTypeId);
        if (!RenameResult || Types.FirstOrDefault(t => t.Id == relationshipTypeId) is not { } type)
        {
            return Task.FromResult(false);
        }

        type.Name = normalized;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid relationshipTypeId, Guid? reassignToId, CancellationToken cancellationToken = default)
    {
        Deleted.Add((relationshipTypeId, reassignToId));
        if (ThrowOnNextDelete is { } exception)
        {
            ThrowOnNextDelete = null;
            throw exception;
        }

        if (!DeleteResult || Types.FirstOrDefault(t => t.Id == relationshipTypeId) is not { } type)
        {
            return Task.FromResult(false);
        }

        if (reassignToId is { } target)
        {
            PeopleCounts[target] = PeopleCounts.GetValueOrDefault(target) + PeopleCounts.GetValueOrDefault(type.Id);
        }

        Types.Remove(type);
        PeopleCounts.Remove(type.Id);
        return Task.FromResult(true);
    }

    private string Validate(string? name, Guid? ownId)
    {
        var normalized = LabelNameRules.Normalize(name);
        if (LabelNameRules.Validate(normalized, RelationshipType.NameMaxLength) is { } error)
        {
            throw new LabelValidationException(error);
        }

        if (Types.Any(t => t.Id != ownId && LabelNameRules.Comparer.Equals(t.Name, normalized)))
        {
            throw new LabelValidationException(LabelValidationError.NameTaken);
        }

        return normalized!;
    }
}

/// <summary>
/// An in-memory <see cref="ITagService"/> for component tests, like
/// <see cref="FakeRelationshipTypeService"/>: lists <see cref="Tags"/> (counts in
/// <see cref="PeopleCounts"/>), applies the real name rules and records every mutation.
/// </summary>
internal sealed class FakeTagService : ITagService
{
    public List<Tag> Tags { get; } = [];

    /// <summary>How many people have each tag (by id); a tag not in here has none.</summary>
    public Dictionary<Guid, int> PeopleCounts { get; } = [];

    public int ListCalls { get; private set; }

    public int ListWithUsageCalls { get; private set; }

    public List<string?> Created { get; } = [];

    public List<(Guid Id, string? Name)> Renamed { get; } = [];

    public List<Guid> Deleted { get; } = [];

    /// <summary>What <see cref="RenameAsync"/> returns when the rules accept the name: <see langword="false"/> means the tag is gone.</summary>
    public bool RenameResult { get; set; } = true;

    /// <summary>What <see cref="DeleteAsync"/> returns: <see langword="false"/> means the tag is gone.</summary>
    public bool DeleteResult { get; set; } = true;

    /// <summary>Adds a tag to the list, with the number of people who have it.</summary>
    public Tag Add(string name, int people = 0)
    {
        var tag = new Tag { Id = Guid.NewGuid(), OwnerId = "owner", Name = name };
        Tags.Add(tag);
        PeopleCounts[tag.Id] = people;
        return tag;
    }

    public Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken = default)
    {
        ListCalls++;
        return Task.FromResult<IReadOnlyList<Tag>>(Tags.ToList());
    }

    public Task<IReadOnlyList<TagUsage>> ListWithUsageAsync(CancellationToken cancellationToken = default)
    {
        ListWithUsageCalls++;
        return Task.FromResult<IReadOnlyList<TagUsage>>(Tags
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TagUsage(t.Id, t.Name, PeopleCounts.GetValueOrDefault(t.Id)))
            .ToList());
    }

    public Task<Tag> CreateAsync(string? name, CancellationToken cancellationToken = default)
    {
        Created.Add(name);
        return Task.FromResult(Add(Validate(name, null)));
    }

    public Task<bool> RenameAsync(Guid tagId, string? name, CancellationToken cancellationToken = default)
    {
        Renamed.Add((tagId, name));
        var normalized = Validate(name, tagId);
        if (!RenameResult || Tags.FirstOrDefault(t => t.Id == tagId) is not { } tag)
        {
            return Task.FromResult(false);
        }

        tag.Name = normalized;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid tagId, CancellationToken cancellationToken = default)
    {
        Deleted.Add(tagId);
        if (!DeleteResult || Tags.FirstOrDefault(t => t.Id == tagId) is not { } tag)
        {
            return Task.FromResult(false);
        }

        Tags.Remove(tag);
        PeopleCounts.Remove(tag.Id);
        return Task.FromResult(true);
    }

    private string Validate(string? name, Guid? ownId)
    {
        var normalized = LabelNameRules.Normalize(name);
        if (LabelNameRules.Validate(normalized, Tag.NameMaxLength) is { } error)
        {
            throw new LabelValidationException(error);
        }

        if (Tags.Any(t => t.Id != ownId && LabelNameRules.Comparer.Equals(t.Name, normalized)))
        {
            throw new LabelValidationException(LabelValidationError.NameTaken);
        }

        return normalized!;
    }
}

/// <summary>
/// An in-memory <see cref="IReminderService"/> for component tests.
/// </summary>
internal sealed class FakeReminderService(FakePeopleService? peopleService = null) : Relio.Application.Reminders.IReminderService
{
    public List<Relio.Application.Reminders.ReminderDto> Reminders { get; } = [];

    public Task<Relio.Application.Reminders.ReminderDto?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Reminders.FirstOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<Relio.Application.Reminders.ReminderDto>> ListAsync(bool includeCompleted = false, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Relio.Application.Reminders.ReminderDto>>(
            Reminders.Where(r => includeCompleted || !r.IsCompleted).ToList());

    public Task<IReadOnlyList<Relio.Application.Reminders.ReminderDto>> ListForPersonAsync(Guid personId, bool includeCompleted = false, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Relio.Application.Reminders.ReminderDto>>(
            Reminders.Where(r => r.PersonId == personId && (includeCompleted || !r.IsCompleted)).ToList());

    public Task<IReadOnlyList<Relio.Application.Reminders.ReminderDto>> ListDueAsync(DateOnly onOrBeforeDate, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Relio.Application.Reminders.ReminderDto>>(
            Reminders.Where(r => !r.IsCompleted && r.EffectiveDueDate <= onOrBeforeDate).ToList());

    public Task<Relio.Application.Reminders.ReminderDto> CreateAsync(Relio.Application.Reminders.CreateReminderRequest request, CancellationToken cancellationToken = default)
    {
        var dto = new Relio.Application.Reminders.ReminderDto(
            Guid.NewGuid(),
            request.PersonId,
            "Person",
            request.Title,
            request.DueDate,
            request.Frequency,
            request.CustomIntervalMonths,
            null,
            request.DueDate,
            false,
            null,
            null);
        Reminders.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<Relio.Application.Reminders.ReminderDto?> UpdateAsync(Guid id, Relio.Application.Reminders.UpdateReminderRequest request, CancellationToken cancellationToken = default)
    {
        var existing = Reminders.FirstOrDefault(r => r.Id == id);
        if (existing is null)
        {
            return Task.FromResult<Relio.Application.Reminders.ReminderDto?>(null);
        }

        Reminders.Remove(existing);
        var updated = existing with
        {
            Title = request.Title,
            DueDate = request.DueDate,
            Frequency = request.Frequency,
            CustomIntervalMonths = request.CustomIntervalMonths,
            EffectiveDueDate = existing.SnoozedUntilDate ?? request.DueDate,
        };
        Reminders.Add(updated);
        return Task.FromResult<Relio.Application.Reminders.ReminderDto?>(updated);
    }

    public Task<bool> CompleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = Reminders.FirstOrDefault(r => r.Id == id);
        if (existing is null)
        {
            return Task.FromResult(false);
        }

        Reminders.Remove(existing);
        Reminders.Add(existing with { IsCompleted = true, CompletedAtUtc = DateTime.UtcNow });
        return Task.FromResult(true);
    }

    public Task<bool> SnoozeAsync(Guid id, DateOnly snoozedUntilDate, CancellationToken cancellationToken = default)
    {
        var existing = Reminders.FirstOrDefault(r => r.Id == id);
        if (existing is null)
        {
            return Task.FromResult(false);
        }

        Reminders.Remove(existing);
        Reminders.Add(existing with { SnoozedUntilDate = snoozedUntilDate, EffectiveDueDate = snoozedUntilDate });
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var removed = Reminders.RemoveAll(r => r.Id == id);
        return Task.FromResult(removed > 0);
    }

    public List<Relio.Application.Reminders.BirthdayReminderDto> BirthdayReminders { get; } = [];

    public Task<IReadOnlyList<Relio.Application.Reminders.BirthdayReminderDto>> ListDueBirthdaysAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Relio.Application.Reminders.BirthdayReminderDto>>(
            BirthdayReminders.Where(b => b.IsDue).ToList());

    public Task<IReadOnlyList<Relio.Application.Reminders.BirthdayReminderDto>> ListUpcomingBirthdaysAsync(int daysAhead = 30, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Relio.Application.Reminders.BirthdayReminderDto>>(
            BirthdayReminders.Where(b => b.DaysUntilBirthday >= 0 && b.DaysUntilBirthday <= daysAhead).ToList());

    public List<Relio.Application.Reminders.ReachOutDto> OverdueReachOuts { get; } = [];

    public List<Guid> ContactedPersonIds { get; } = [];

    public Task<IReadOnlyList<Relio.Application.Reminders.ReachOutDto>> ListOverdueReachOutsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Relio.Application.Reminders.ReachOutDto>>(OverdueReachOuts);

    public Task<bool> MarkContactedAsync(Guid personId, DateOnly? contactedOn = null, CancellationToken cancellationToken = default)
    {
        ContactedPersonIds.Add(personId);
        OverdueReachOuts.RemoveAll(r => r.PersonId == personId);
        if (peopleService?.Known.FirstOrDefault(p => p.Id == personId) is { } person)
        {
            person.LastContactedOn = contactedOn ?? DateOnly.FromDateTime(DateTime.UtcNow);
        }
        return Task.FromResult(true);
    }
}
