using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Relio.Application.Security;

namespace Relio.Web.Security;

/// <summary>
/// <see cref="ICurrentUser"/> implementation for the hosted app, backed by
/// <see cref="AuthenticationStateProvider"/> rather than <c>HttpContext</c>. Scoped (registered
/// per-request/per-circuit in <c>Program.cs</c>), like <see cref="AuthenticationStateProvider"/>
/// itself.
/// </summary>
/// <remarks>
/// <c>IHttpContextAccessor.HttpContext</c> is only populated for the duration of the initial HTTP
/// request/prerender; once a Blazor Server circuit's SignalR connection takes over, it is
/// <see langword="null"/> for the rest of the circuit's lifetime - any <c>ICurrentUser</c> built
/// on it (the previous <c>HttpContextCurrentUser</c>) silently stops resolving the signed-in user
/// as soon as the circuit connects. <see cref="AuthenticationStateProvider"/> does not have this
/// problem: for Blazor Server, the framework's own provider captures the user once when the
/// circuit is created (from the same <c>HttpContext</c> that served the initial request) and
/// holds it for the circuit's lifetime, so <see cref="AuthenticationStateProvider.GetAuthenticationStateAsync"/>
/// keeps returning the right principal throughout.
///
/// That method is still asynchronous in signature, while every existing Application service reads
/// <see cref="ICurrentUser"/> synchronously (<c>RequireUserId()</c>, called inline inside already-async
/// service methods - see the "User-scoped data pattern" section of AGENTS.md). Rather than make
/// every service and its tests async-aware of a second layer of asynchrony, this blocks on the
/// call with <c>GetAwaiter().GetResult()</c>. That is safe here specifically because the
/// framework's provider never actually awaits anything to produce the result - it returns an
/// already-completed <see cref="Task{TResult}"/> for the whole circuit, so the block returns
/// immediately and cannot deadlock. Verified end-to-end by <c>Relio.Web.E2ETests</c> (sign in, then
/// exercise a service call from an interactive component).
/// </remarks>
public sealed class AuthenticationStateCurrentUser(AuthenticationStateProvider authenticationStateProvider)
    : ICurrentUser
{
    /// <inheritdoc />
    public bool IsAuthenticated => GetUser().Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public string? UserId => IsAuthenticated ? GetUser().FindFirstValue(ClaimTypes.NameIdentifier) : null;

    private ClaimsPrincipal GetUser() =>
        authenticationStateProvider.GetAuthenticationStateAsync().GetAwaiter().GetResult().User;
}
