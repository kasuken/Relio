namespace Relio.Web.Security;

/// <summary>Configures per-peer throttling for sensitive account form posts.</summary>
/// <remarks>
/// <c>HttpContext.Connection.RemoteIpAddress</c> is used, never a raw forwarded-header value. Behind a
/// reverse proxy it is the proxy's address (one shared bucket for the whole site) unless
/// <c>Security:ForwardedHeaders:Enabled</c> is set, which makes the forwarded-headers middleware
/// replace it with the real client address (see <see cref="ForwardedHeadersSettings"/>). Buckets are process-local, so multiple app instances have independent budgets.
/// </remarks>
public sealed class RateLimitingOptions
{
    /// <summary>The configuration section containing these options.</summary>
    public const string SectionName = "Security:RateLimiting";

    /// <summary>The number of queued requests. Sensitive account requests are never queued.</summary>
    public const int QueueLimit = 0;

    /// <summary>Whether account-form throttling is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum login, second-factor, and account-deletion reauthentication form posts per peer
    /// during <see cref="LoginWindow"/>.
    /// </summary>
    public int LoginPermitLimit { get; set; } = 10;

    /// <summary>Window applied to login and second-factor form posts.</summary>
    public TimeSpan LoginWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Maximum registration form posts per peer during <see cref="RegistrationWindow"/>.</summary>
    public int RegistrationPermitLimit { get; set; } = 5;

    /// <summary>Window applied to registration form posts.</summary>
    public TimeSpan RegistrationWindow { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Maximum password-recovery form posts per peer during <see cref="PasswordResetWindow"/>.</summary>
    public int PasswordResetPermitLimit { get; set; } = 5;

    /// <summary>Window shared by forgot-password and reset-password form posts.</summary>
    public TimeSpan PasswordResetWindow { get; set; } = TimeSpan.FromMinutes(10);
}
