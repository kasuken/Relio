using Relio.Application.People;
using Relio.Domain;

namespace Relio.Data.People;

/// <summary>
/// The one place a <see cref="Person"/> and its <see cref="ContactMethod"/>s are built from a profile
/// input: <c>PeopleService.CreateAsync</c> and <c>UpdateAsync</c> (#22, #24) and the import (#29) all
/// use it, so a person created by the form and one created by an import are stored identically
/// (normalized text, comparison keys, positions).
/// </summary>
/// <remarks>
/// Nothing here touches the database or the change tracker, and nothing is logged. Tags and the
/// relationship type are the caller's business: they are foreign ids that the caller resolves against
/// the owner's rows first.
/// </remarks>
internal static class PersonEntityBuilder
{
    /// <summary>
    /// A new person owned by <paramref name="ownerId"/> with the profile of <paramref name="input"/>
    /// and one contact method per input row, in order. Not tracked; the caller adds it to the context.
    /// </summary>
    public static Person NewPerson(string ownerId, IPersonProfileInput input)
    {
        var person = new Person { OwnerId = ownerId };
        ApplyProfile(person, input);

        var contactMethods = input.ContactMethods ?? [];
        for (var position = 0; position < contactMethods.Count; position++)
        {
            person.ContactMethods.Add(CreateContactMethod(ownerId, contactMethods[position], position));
        }

        return person;
    }

    public static ContactMethod CreateContactMethod(string ownerId, ContactMethodInput input, int sortOrder)
    {
        var contactMethod = new ContactMethod { OwnerId = ownerId };
        ApplyContactMethod(contactMethod, input, sortOrder);
        return contactMethod;
    }

    /// <summary>
    /// Writes a submitted row onto <paramref name="contactMethod"/>: the trimmed value, the label,
    /// the comparison key from <see cref="ContactMethodRules.ToNormalizedValue"/> and the position.
    /// </summary>
    public static void ApplyContactMethod(ContactMethod contactMethod, ContactMethodInput input, int sortOrder)
    {
        var value = ContactMethodRules.NormalizeValue(input.Kind, input.Value);
        contactMethod.Kind = input.Kind;
        contactMethod.Label = ContactMethodRules.NormalizeLabel(input.Label);
        contactMethod.Value = value;
        contactMethod.NormalizedValue = ContactMethodRules.ToNormalizedValue(input.Kind, value);
        contactMethod.SortOrder = sortOrder;
    }

    /// <summary>Writes every profile field of <paramref name="input"/> onto <paramref name="person"/>, normalized.</summary>
    public static void ApplyProfile(Person person, IPersonProfileInput input)
    {
        person.FirstName = PersonProfileRules.NormalizeRequired(input.FirstName);
        person.LastName = PersonProfileRules.NormalizeOptional(input.LastName);
        person.Nickname = PersonProfileRules.NormalizeOptional(input.Nickname);
        person.RelationshipTypeId = input.RelationshipTypeId;
        person.HowWeMet = PersonProfileRules.NormalizeOptional(input.HowWeMet);
        person.Details = PersonProfileRules.NormalizeOptional(input.Details);

        // Validation guarantees day and month come together, and that a year never comes alone.
        var hasBirthday = input.BirthdayDay is not null && input.BirthdayMonth is not null;
        person.BirthdayDay = hasBirthday ? input.BirthdayDay : null;
        person.BirthdayMonth = hasBirthday ? input.BirthdayMonth : null;
        person.BirthdayYear = hasBirthday ? input.BirthdayYear : null;

        person.StayInTouchCadenceDays = input.StayInTouchCadenceDays;
        person.BirthdayReminderDisabled = input.BirthdayReminderDisabled;
        person.BirthdayReminderLeadDays = input.BirthdayReminderLeadDays;
    }
}
