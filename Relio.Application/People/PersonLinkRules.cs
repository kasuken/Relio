using Relio.Domain;

namespace Relio.Application.People;

/// <summary>
/// The rules for linking two people that depend only on the input. Pure; the service is the
/// authority, the page never repeats these.
/// </summary>
public static class PersonLinkRules
{
    /// <summary>
    /// Validates a link request. A <see cref="Guid.Empty"/> <paramref name="personId"/> is not a
    /// code: it is a foreign-id failure the service reports like any other unknown person.
    /// </summary>
    public static IReadOnlyList<PersonLinkValidationError> Validate(
        Guid personId,
        Guid relatedPersonId,
        PersonLinkKind? kind)
    {
        var errors = new List<PersonLinkValidationError>();

        if (relatedPersonId == Guid.Empty)
        {
            errors.Add(PersonLinkValidationError.PersonRequired);
        }
        else if (relatedPersonId == personId)
        {
            errors.Add(PersonLinkValidationError.SelfLink);
        }

        errors.AddRange(ValidateKind(kind));
        return errors;
    }

    /// <summary>Validates the kind alone, as an update does.</summary>
    public static IReadOnlyList<PersonLinkValidationError> ValidateKind(PersonLinkKind? kind)
    {
        if (kind is null)
        {
            return [PersonLinkValidationError.KindRequired];
        }

        return Enum.IsDefined(kind.Value) ? [] : [PersonLinkValidationError.KindInvalid];
    }
}
