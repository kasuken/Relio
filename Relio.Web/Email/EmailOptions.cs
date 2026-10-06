namespace Relio.Web.Email;

/// <summary>
/// Email configuration, bound from the <c>Email</c> configuration section. See
/// <c>ServiceCollectionExtensions.AddRelioIdentity</c> for how <see cref="Provider"/> selects the
/// <c>IEmailSender&lt;RelioUser&gt;</c> implementation.
/// </summary>
public sealed class EmailOptions
{
    /// <summary>The configuration section name (<c>Email</c>).</summary>
    public const string SectionName = "Email";

    /// <summary>No email provider configured (the default). See <see cref="Provider"/>.</summary>
    public const string NoneProvider = "None";

    /// <summary>SMTP email provider. See <see cref="Provider"/> and <see cref="Smtp"/>.</summary>
    public const string SmtpProvider = "Smtp";

    /// <summary>
    /// Which email provider to use: <see cref="NoneProvider"/> (the default - self-hosted Relio
    /// works with no email provider at all, see epic #14's guardrails) or <see cref="SmtpProvider"/>.
    /// </summary>
    public string Provider { get; set; } = NoneProvider;

    /// <summary>
    /// Whether this instance can actually deliver email (<see cref="Provider"/> is
    /// <see cref="SmtpProvider"/>, case-insensitively). Get-only, so configuration binding ignores it.
    /// Pages use it to decide whether a flow that needs an emailed link (password reset, changing
    /// the email address) is available.
    /// </summary>
    public bool CanSendEmail => string.Equals(Provider, SmtpProvider, StringComparison.OrdinalIgnoreCase);

    /// <summary>SMTP settings, used only when <see cref="Provider"/> is <see cref="SmtpProvider"/>.</summary>
    public SmtpOptions Smtp { get; set; } = new();
}

/// <summary>
/// SMTP connection settings. Never put <see cref="Password"/> in <c>appsettings*.json</c> - use
/// user secrets locally (<c>dotnet user-secrets set Email:Smtp:Password "..."</c>) or the
/// hosting platform's secret store/environment variables (<c>Email__Smtp__Password</c>) in
/// production (see the gdpr-compliant skill and AGENTS.md "Privacy").
/// </summary>
public sealed class SmtpOptions
{
    /// <summary>The SMTP server host name.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>The SMTP server port. Defaults to 587 (STARTTLS submission).</summary>
    public int Port { get; set; } = 587;

    /// <summary>Whether to use TLS. Defaults to <see langword="true"/>.</summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>The SMTP authentication user name.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// The SMTP authentication password. Never set this in <c>appsettings*.json</c> - see the
    /// type-level remarks.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>The address Relio's account emails are sent from.</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>The display name Relio's account emails are sent from.</summary>
    public string FromName { get; set; } = "Relio";
}
