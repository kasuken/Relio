namespace Relio.Application.Security;

/// <summary>
/// Abstraction over the signed-in user, used by Application services so they never depend on
/// <c>HttpContext</c> directly (see <c>.github/instructions/techstack.instructions.md</c>). The
/// Web project supplies the real implementation, backed by <c>HttpContext</c> claims; tests
/// supply a fake. See the "User-scoped data pattern" section of AGENTS.md.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Whether a user is currently signed in.</summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// The signed-in user's id (matches the future ASP.NET Core Identity user id, epic #14), or
    /// <see langword="null"/> when <see cref="IsAuthenticated"/> is <see langword="false"/>.
    /// </summary>
    string? UserId { get; }
}
