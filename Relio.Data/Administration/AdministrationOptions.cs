namespace Relio.Data.Administration;

/// <summary>
/// Operator-level administration settings, bound from the <c>Administration</c> configuration
/// section (issue #19). Deliberately absent from <c>appsettings.json</c>: it names a person, so it
/// belongs in an environment variable or the host's secret store.
/// </summary>
public sealed class AdministrationOptions
{
    /// <summary>The configuration section name (<c>Administration</c>).</summary>
    public const string SectionName = "Administration";

    /// <summary>
    /// The email address of an existing account to promote to Administrator at startup
    /// (<c>Administration:AdministratorEmail</c>). The way to get an Administrator on an instance
    /// that was running before issue #19 (its accounts predate the first-account rule), or to
    /// recover when nobody is one. Unset by default. Idempotent; does nothing when no account has
    /// that address. See <see cref="AdministratorBootstrapper"/>.
    /// </summary>
    public string? AdministratorEmail { get; set; }
}
