using Relio.Application.People;

namespace Relio.Web.Components.People;

/// <summary>
/// The mutable values the person form's inputs bind to. A separate class from the request records
/// because those are immutable and their <c>FirstName</c> is required, while a half-typed form has
/// neither. Create now; the edit page (issue #24) will add a way to start from an existing person.
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

    /// <summary>"How you met", as typed.</summary>
    public string? HowWeMet { get; set; }

    /// <summary>Details, as typed.</summary>
    public string? Details { get; set; }

    /// <summary>Builds the request for <c>IPeopleService.CreateAsync</c>. Nothing is trimmed here - the service does that.</summary>
    public CreatePersonRequest ToCreateRequest() => new()
    {
        FirstName = FirstName ?? string.Empty,
        LastName = LastName,
        Nickname = Nickname,
        RelationshipTypeId = RelationshipTypeId,
        BirthdayDay = BirthdayDay,
        BirthdayMonth = BirthdayMonth,
        BirthdayYear = BirthdayYear,
        HowWeMet = HowWeMet,
        Details = Details,
    };
}
