using Relio.Domain;

namespace Relio.Application.People;

/// <summary>
/// One contact method as the person form submits it. A request carries the person's <b>whole</b>
/// list of these; see <see cref="UpdatePersonRequest.ContactMethods"/> for how it is applied.
/// </summary>
/// <param name="Id">
/// The id of the existing contact method this row edits, or <see langword="null"/> for a new one.
/// An id that is not one of this person's contact methods makes the service throw
/// <see cref="Relio.Application.Ownership.ForeignEntityNotOwnedException"/>.
/// </param>
/// <param name="Kind">What sort of detail this is.</param>
/// <param name="Label">An optional label such as "Work". Trimmed; blank means none.</param>
/// <param name="Value">The detail itself. Trimmed; required. Personal data: never log it.</param>
public sealed record ContactMethodInput(Guid? Id, ContactMethodKind Kind, string? Label, string? Value);
