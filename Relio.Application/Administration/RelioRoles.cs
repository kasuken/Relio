namespace Relio.Application.Administration;

/// <summary>
/// The ASP.NET Core Identity role names Relio uses. There is exactly one: self-hosted
/// administration (issue #19) needs "can manage who uses this instance" and nothing finer. See the
/// "Self-hosted administration" bullet of AGENTS.md.
/// </summary>
public static class RelioRoles
{
    /// <summary>
    /// Manages accounts and sign-up for the instance (see <see cref="IUserAdministrationService"/>).
    /// An Administrator never gets access to anyone's people, notes or moments - this role is about
    /// accounts, not content.
    /// </summary>
    public const string Administrator = "Administrator";
}
