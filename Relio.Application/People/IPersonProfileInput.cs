namespace Relio.Application.People;

/// <summary>
/// The profile fields shared by <see cref="CreatePersonRequest"/> and <see cref="UpdatePersonRequest"/>,
/// so <see cref="PersonProfileRules"/> validates both the same way.
/// </summary>
public interface IPersonProfileInput
{
    /// <summary>The first name. Required.</summary>
    string FirstName { get; }

    /// <summary>The last name. Optional.</summary>
    string? LastName { get; }

    /// <summary>What the user calls the person. Optional.</summary>
    string? Nickname { get; }

    /// <summary>One of the current user's relationship type ids, or null for none.</summary>
    Guid? RelationshipTypeId { get; }

    /// <summary>The day of the birthday (1 to 31). Needs <see cref="BirthdayMonth"/>.</summary>
    int? BirthdayDay { get; }

    /// <summary>The month of the birthday (1 to 12). Needs <see cref="BirthdayDay"/>.</summary>
    int? BirthdayMonth { get; }

    /// <summary>The year of birth. Optional, and only with a day and a month.</summary>
    int? BirthdayYear { get; }

    /// <summary>How the user met the person. Optional.</summary>
    string? HowWeMet { get; }

    /// <summary>Anything else worth remembering. Optional.</summary>
    string? Details { get; }
}
