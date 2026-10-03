using System.Security.Claims;
using Relio.Application.Security;

namespace Relio.Web.Security;

/// <summary>
/// <see cref="ICurrentUser"/> implementation for the hosted app, backed by the current
/// <see cref="HttpContext"/>'s claims principal. Reads the user id from the standard
/// <see cref="ClaimTypes.NameIdentifier"/> claim, which ASP.NET Core Identity populates (epic
/// #14). Scoped per-request/per-circuit; registered in <c>Program.cs</c>.
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    /// <inheritdoc />
    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public string? UserId => IsAuthenticated
        ? httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        : null;
}
