namespace Relio.Application.Administration;

/// <summary>
/// Sign-up control, bound from the <c>Registration</c> configuration section (issue #19). Parsed
/// and validated eagerly at startup by <c>Relio.Web.Identity.ServiceCollectionExtensions.BuildRegistrationOptions</c>,
/// so a typo in <c>Registration:Mode</c> fails fast instead of silently leaving sign-up open.
/// </summary>
public sealed class RegistrationOptions
{
    /// <summary>The configuration section name (<c>Registration</c>).</summary>
    public const string SectionName = "Registration";

    /// <summary>Who may create an account. Defaults to <see cref="RegistrationMode.Open"/>.</summary>
    public RegistrationMode Mode { get; set; } = RegistrationMode.Open;

    /// <summary>
    /// How long an invitation link stays valid after an administrator creates it. Defaults to 7
    /// days. Must be greater than zero.
    /// </summary>
    public TimeSpan InvitationLifetime { get; set; } = TimeSpan.FromDays(7);
}
