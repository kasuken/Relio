namespace Relio.Application.Security;

/// <summary>
/// Thrown by Application services when an operation that requires a signed-in user is invoked
/// with no authenticated <c>ICurrentUser</c>. Callers (Blazor components, future endpoints) are
/// expected to prevent this by gating access to authenticated users; this exception is a
/// defensive backstop, not a user-facing error.
/// </summary>
public sealed class UnauthenticatedUserException()
    : Exception("The current operation requires a signed-in user.");
