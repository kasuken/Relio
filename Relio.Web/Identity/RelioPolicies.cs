namespace Relio.Web.Identity;

/// <summary>Names of Relio's authorization policies (registered in <see cref="ServiceCollectionExtensions.AddRelioIdentity"/>).</summary>
public static class RelioPolicies
{
    /// <summary>
    /// Signed in and in the Relio.Application.Administration.RelioRoles.Administrator role (issue
    /// #19). A UI gate only: <c>IUserAdministrationService</c> re-checks the role and the disabled
    /// flag against the database on every call.
    /// </summary>
    public const string Administrator = "Administrator";
}
