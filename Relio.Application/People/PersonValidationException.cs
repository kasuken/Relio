namespace Relio.Application.People;

/// <summary>
/// Thrown by <c>IPeopleService.CreateAsync</c>/<c>UpdateAsync</c> when the profile input breaks a
/// rule in <see cref="PersonProfileRules"/>. Nothing was saved. The message lists the error codes
/// only - never the submitted values - so it is safe to log (see the gdpr-compliant skill).
/// </summary>
public sealed class PersonValidationException(IReadOnlyList<PersonValidationError> errors)
    : Exception($"The person could not be saved: {string.Join(", ", errors)}.")
{
    /// <summary>Every rule the input broke, in the order they were checked.</summary>
    public IReadOnlyList<PersonValidationError> Errors { get; } = errors;
}
