namespace Relio.Application.Security;

/// <summary>Convenience helpers for <see cref="ICurrentUser"/>.</summary>
public static class CurrentUserExtensions
{
    /// <summary>
    /// Returns the signed-in user's id, or throws <see cref="UnauthenticatedUserException"/> when
    /// no user is signed in. Every service method that reads or mutates user-owned data should
    /// call this first, before touching the data store.
    /// </summary>
    public static string RequireUserId(this ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.UserId))
        {
            throw new UnauthenticatedUserException();
        }

        return currentUser.UserId;
    }
}
