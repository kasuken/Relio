namespace Relio.Application.Administration;

/// <summary>
/// Thrown by <see cref="IUserAdministrationService"/> when the caller is not an active
/// Administrator. The check is made against the database on every call - never against claims
/// cached in a session - so a demoted or disabled administrator loses access immediately. Callers
/// are expected to gate the UI with the Administrator policy; this exception is the defensive
/// backstop.
/// </summary>
public sealed class AdministratorRequiredException()
    : Exception("The current operation requires an active Administrator.");
