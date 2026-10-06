namespace Relio.Application.People;

/// <summary>A rule one submitted contact method broke: which row (by its position in the request) and why.</summary>
/// <param name="Index">The zero-based position of the row in the submitted list.</param>
/// <param name="Error">The rule that was broken.</param>
public sealed record ContactMethodProblem(int Index, ContactMethodValidationError Error);
