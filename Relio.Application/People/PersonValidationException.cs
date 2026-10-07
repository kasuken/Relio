namespace Relio.Application.People;

/// <summary>
/// Thrown by <c>IPeopleService.CreateAsync</c>/<c>UpdateAsync</c> when the input breaks a rule in
/// <see cref="PersonProfileRules"/> or <see cref="ContactMethodRules"/>. Nothing was saved. The
/// message lists the error codes (and the index of a contact method row) only - never the
/// submitted values - so it is safe to log (see the gdpr-compliant skill).
/// </summary>
public sealed class PersonValidationException : Exception
{
    /// <summary>Creates the exception from the profile errors and, optionally, per-row contact method problems.</summary>
    public PersonValidationException(
        IReadOnlyList<PersonValidationError> errors,
        IReadOnlyList<ContactMethodProblem>? contactMethodProblems = null)
        : base(BuildMessage(errors, contactMethodProblems ?? []))
    {
        Errors = errors;
        ContactMethodProblems = contactMethodProblems ?? [];
    }

    /// <summary>Every profile-level rule the input broke, in the order they were checked.</summary>
    public IReadOnlyList<PersonValidationError> Errors { get; }

    /// <summary>Every rule a contact method row broke, with the row's index; empty when none did.</summary>
    public IReadOnlyList<ContactMethodProblem> ContactMethodProblems { get; }

    private static string BuildMessage(
        IReadOnlyList<PersonValidationError> errors, IReadOnlyList<ContactMethodProblem> problems)
    {
        var codes = errors.Select(error => error.ToString())
            .Concat(problems.Select(problem => $"ContactMethods[{problem.Index}].{problem.Error}"));
        return $"The person could not be saved: {string.Join(", ", codes)}.";
    }
}
