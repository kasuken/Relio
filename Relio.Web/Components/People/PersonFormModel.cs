using Relio.Application.People;
using Relio.Domain;

namespace Relio.Web.Components.People;

/// <summary>
/// One contact method row of the person form, as the inputs bind to it. A class (not a record)
/// because the inputs write to it, with its own <see cref="Key"/> so a row keeps its identity - and
/// its error messages - while other rows are added or removed around it.
/// </summary>
public sealed class ContactMethodFormRow
{
    /// <summary>A key that never changes for the life of the row (the <c>@key</c> and the error lookup). Not the database id: a new row has none yet.</summary>
    public Guid Key { get; } = Guid.NewGuid();

    /// <summary>The id of the saved contact method this row edits, or null for a row that is new.</summary>
    public Guid? Id { get; init; }

    /// <summary>What sort of detail this is.</summary>
    public ContactMethodKind Kind { get; set; } = ContactMethodKind.Email;

    /// <summary>The optional label, as typed.</summary>
    public string? Label { get; set; }

    /// <summary>The value, as typed.</summary>
    public string? Value { get; set; }

    /// <summary>True when the row has neither a value nor a label: an untouched new row is not sent.</summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(Value) && string.IsNullOrWhiteSpace(Label);
}

/// <summary>
/// The mutable values the person form's inputs bind to. A separate class from the request records
/// because those are immutable and their <c>FirstName</c> is required, while a half-typed form has
/// neither. <see cref="FromPerson"/> starts it from an existing person (the edit page, issue #24);
/// <c>new</c> starts it empty (the add page).
/// </summary>
public sealed class PersonFormModel
{
    /// <summary>First name, as typed.</summary>
    public string? FirstName { get; set; }

    /// <summary>Last name, as typed.</summary>
    public string? LastName { get; set; }

    /// <summary>Nickname, as typed.</summary>
    public string? Nickname { get; set; }

    /// <summary>The chosen relationship type id, or null for none.</summary>
    public Guid? RelationshipTypeId { get; set; }

    /// <summary>Birthday day, as typed.</summary>
    public int? BirthdayDay { get; set; }

    /// <summary>Birthday month, as chosen (1 to 12).</summary>
    public int? BirthdayMonth { get; set; }

    /// <summary>Birthday year, as typed.</summary>
    public int? BirthdayYear { get; set; }

    /// <summary>Whether birthday reminders are disabled specifically for this person (issue #38).</summary>
    public bool BirthdayReminderDisabled { get; set; }

    /// <summary>Per-person lead time override for birthday reminders in days (issue #38).</summary>
    public int? BirthdayReminderLeadDays { get; set; }

    /// <summary>"How you met", as typed.</summary>
    public string? HowWeMet { get; set; }

    /// <summary>Details, as typed.</summary>
    public string? Details { get; set; }

    /// <summary>The contact method rows, in the order shown.</summary>
    public List<ContactMethodFormRow> ContactMethods { get; } = [];

    /// <summary>The chosen tags: existing ones (with an id) and names that will be created (without).</summary>
    public List<TagSelection> Tags { get; } = [];

    /// <summary>
    /// A model holding what <paramref name="person"/> has now: its profile fields, its contact
    /// methods in <see cref="ContactMethod.SortOrder"/> order (each remembering the id it edits)
    /// and its tags by name.
    /// </summary>
    public static PersonFormModel FromPerson(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);

        var model = new PersonFormModel
        {
            FirstName = person.FirstName,
            LastName = person.LastName,
            Nickname = person.Nickname,
            RelationshipTypeId = person.RelationshipTypeId,
            BirthdayDay = person.BirthdayDay,
            BirthdayMonth = person.BirthdayMonth,
            BirthdayYear = person.BirthdayYear,
            BirthdayReminderDisabled = person.BirthdayReminderDisabled,
            BirthdayReminderLeadDays = person.BirthdayReminderLeadDays,
            HowWeMet = person.HowWeMet,
            Details = person.Details,
        };

        model.ContactMethods.AddRange(person.ContactMethods
            .OrderBy(contactMethod => contactMethod.SortOrder)
            .Select(contactMethod => new ContactMethodFormRow
            {
                Id = contactMethod.Id,
                Kind = contactMethod.Kind,
                Label = contactMethod.Label,
                Value = contactMethod.Value,
            }));

        model.Tags.AddRange(person.Tags
            .OrderBy(tag => tag.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(tag => new TagSelection(tag.Id, tag.Name)));

        return model;
    }

    /// <summary>Builds the request for <c>IPeopleService.CreateAsync</c>. Nothing is trimmed here - the service does that.</summary>
    public CreatePersonRequest ToCreateRequest() => ToCreateRequest(out _);

    /// <summary>
    /// Builds the request for <c>IPeopleService.CreateAsync</c>. <paramref name="sentRows"/> are the
    /// rows that went into <see cref="CreatePersonRequest.ContactMethods"/>, in order, so a
    /// problem's index can be traced back to the row it belongs to.
    /// </summary>
    public CreatePersonRequest ToCreateRequest(out IReadOnlyList<ContactMethodFormRow> sentRows) => new()
    {
        FirstName = FirstName ?? string.Empty,
        LastName = LastName,
        Nickname = Nickname,
        RelationshipTypeId = RelationshipTypeId,
        BirthdayDay = BirthdayDay,
        BirthdayMonth = BirthdayMonth,
        BirthdayYear = BirthdayYear,
        BirthdayReminderDisabled = BirthdayReminderDisabled,
        BirthdayReminderLeadDays = BirthdayReminderLeadDays,
        HowWeMet = HowWeMet,
        Details = Details,
        ContactMethods = BuildContactMethods(out sentRows),
        TagIds = TagIds(),
        NewTagNames = NewTagNames(),
    };

    /// <summary>Builds the request for <c>IPeopleService.UpdateAsync</c>; see <see cref="ToCreateRequest(out IReadOnlyList{ContactMethodFormRow})"/>.</summary>
    public UpdatePersonRequest ToUpdateRequest() => ToUpdateRequest(out _);

    /// <summary>Builds the request for <c>IPeopleService.UpdateAsync</c>, reporting the rows that were sent.</summary>
    public UpdatePersonRequest ToUpdateRequest(out IReadOnlyList<ContactMethodFormRow> sentRows) => new()
    {
        FirstName = FirstName ?? string.Empty,
        LastName = LastName,
        Nickname = Nickname,
        RelationshipTypeId = RelationshipTypeId,
        BirthdayDay = BirthdayDay,
        BirthdayMonth = BirthdayMonth,
        BirthdayYear = BirthdayYear,
        BirthdayReminderDisabled = BirthdayReminderDisabled,
        BirthdayReminderLeadDays = BirthdayReminderLeadDays,
        HowWeMet = HowWeMet,
        Details = Details,
        ContactMethods = BuildContactMethods(out sentRows),
        TagIds = TagIds(),
        NewTagNames = NewTagNames(),
    };

    /// <summary>
    /// The rows to send: every row except a <b>new</b> one with neither a value nor a label (the
    /// user added a row and left it alone). A row with only a label is sent, so the service can say
    /// the value is missing, and so is a saved row whose text was cleared - emptying it is not the
    /// same as removing it, and silently deleting a contact method would be a surprise; the service
    /// asks for a value, or for the row to be removed. Nothing is trimmed here.
    /// </summary>
    private List<ContactMethodInput> BuildContactMethods(out IReadOnlyList<ContactMethodFormRow> sentRows)
    {
        var rows = ContactMethods.Where(row => row.Id is not null || !row.IsBlank).ToList();
        sentRows = rows;
        return rows.Select(row => new ContactMethodInput(row.Id, row.Kind, row.Label, row.Value)).ToList();
    }

    private List<Guid> TagIds() => Tags.Where(tag => tag.Id is not null).Select(tag => tag.Id!.Value).ToList();

    private List<string> NewTagNames() => Tags.Where(tag => tag.Id is null).Select(tag => tag.Name).ToList();
}
