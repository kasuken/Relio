using Microsoft.Extensions.Options;

namespace Relio.Web.Security;

/// <summary>Validates account rate-limiting configuration before the app starts.</summary>
public sealed class RateLimitingOptionsValidator : IValidateOptions<RateLimitingOptions>
{
    private static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(30);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        ValidatePolicy(
            nameof(options.LoginPermitLimit),
            options.LoginPermitLimit,
            nameof(options.LoginWindow),
            options.LoginWindow,
            failures);
        ValidatePolicy(
            nameof(options.RegistrationPermitLimit),
            options.RegistrationPermitLimit,
            nameof(options.RegistrationWindow),
            options.RegistrationWindow,
            failures);
        ValidatePolicy(
            nameof(options.PasswordResetPermitLimit),
            options.PasswordResetPermitLimit,
            nameof(options.PasswordResetWindow),
            options.PasswordResetWindow,
            failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidatePolicy(
        string permitLimitName,
        int permitLimit,
        string windowName,
        TimeSpan window,
        ICollection<string> failures)
    {
        if (permitLimit <= 0)
        {
            failures.Add($"{RateLimitingOptions.SectionName}:{permitLimitName} must be greater than zero.");
        }

        if (window <= TimeSpan.Zero || window > MaximumWindow)
        {
            failures.Add(
                $"{RateLimitingOptions.SectionName}:{windowName} must be greater than zero and no more than 30 days.");
        }
    }
}
