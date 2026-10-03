using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
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
    /// <summary>
    /// Maps <c>POST /Account/Logout</c>. Requires the antiforgery token
    /// <c>Program.cs</c>'s <c>UseAntiforgery()</c> middleware already enforces on every non-GET
    /// minimal API endpoint - <c>MainLayout.razor</c>'s sign-out form includes
    /// <c>&lt;AntiforgeryToken /&gt;</c> for exactly this.
    /// </summary>
    public static IEndpointRouteBuilder MapRelioIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var accountGroup = endpoints.MapGroup("/Account");

        accountGroup.MapPost("/Logout", async (
            HttpContext httpContext,
            SignInManager<RelioUser> signInManager,
            ILogger<Program> logger) =>
        {
            // Read before SignOutAsync clears the cookie - logged as an id only, never an email
            // (see AGENTS.md "Privacy"/the gdpr-compliant skill).
            var userId = signInManager.UserManager.GetUserId(httpContext.User);

            await signInManager.SignOutAsync();
            logger.LogInformation("User {UserId} signed out.", userId);

            // Always straight back to the login page - issue #16's acceptance criterion, and
            // simpler/safer than taking a caller-supplied returnUrl back through here only to
            // bounce straight to login again anyway (every page requires sign-in by default).
            return Results.LocalRedirect("/Account/Login");
        }).AllowAnonymous();

        return endpoints;
    }
}
