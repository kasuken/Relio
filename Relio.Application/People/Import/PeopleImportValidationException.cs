namespace Relio.Application.People.Import;

/// <summary>What one request of an import broke: its index in the submitted list and the codes.</summary>
/// <param name="Index">The zero-based position of the request in the submitted list.</param>
/// <param name="Errors">The profile rules it broke.</param>
/// <param name="ContactMethodProblems">The contact method rules it broke, each with its row.</param>
public sealed record ImportRowError(
    int Index,
    IReadOnlyList<PersonValidationError> Errors,
    IReadOnlyList<ContactMethodProblem> ContactMethodProblems);

/// <summary>
/// Thrown by <see cref="IPeopleImportService.ImportAsync"/> when a request breaks a rule in
/// <see cref="PersonProfileRules"/> or <see cref="ContactMethodRules"/>. Nothing was saved. The message
/// lists indexes and codes only - never a value - so it is safe to log (see the gdpr-compliant skill).
/// </summary>
public sealed class PeopleImportValidationException : Exception
{
    /// <summary>Creates the exception from the rows that broke a rule.</summary>
    public PeopleImportValidationException(IReadOnlyList<ImportRowError> rows)
        : base(BuildMessage(rows))
    {
        Rows = rows;
    }

    /// <summary>The rows that broke a rule.</summary>
    public IReadOnlyList<ImportRowError> Rows { get; }

    private static string BuildMessage(IReadOnlyList<ImportRowError> rows)
    {
        var codes = rows.SelectMany(row => row.Errors
            .Select(error => $"[{row.Index}].{error}")
            .Concat(row.ContactMethodProblems.Select(problem => $"[{row.Index}].ContactMethods[{problem.Index}].{problem.Error}")));
        return $"The import could not be saved: {string.Join(", ", codes)}.";
    }
}
