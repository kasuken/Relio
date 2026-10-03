using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Relio.Data.Identity;

namespace Relio.Web.Components.Account;

/// <summary>
/// Maps the non-component Identity endpoints Relio's account pages need. Logout, like
/// registration and login, must write to the HTTP response directly (clearing the auth cookie) -
/// something only a real request/response pair can do, not an interactive Blazor Server circuit
/// (see the "Protect app pages" notes in AGENTS.md) - so it is a plain minimal API endpoint, not a
/// page.
/// </summary>
public static class IdentityComponentsEndpointRouteBuilderExtensions
{
    /// <summary>Maps <c>POST /Account/Logout</c>.</summary>
    public static IEndpointRouteBuilder MapRelioIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var accountGroup = endpoints.MapGroup("/Account");

        accountGroup.MapPost("/Logout", async (
            SignInManager<RelioUser> signInManager,
            [FromForm] string? returnUrl) =>
        {
            await signInManager.SignOutAsync();
            return Results.LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
        }).AllowAnonymous();

        return endpoints;
    }
}
