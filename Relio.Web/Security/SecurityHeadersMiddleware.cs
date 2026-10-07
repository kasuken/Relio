using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Relio.Web.Security;

internal sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var nonce = ContentSecurityPolicyNonce.Create();
        ContentSecurityPolicyNonce.Set(context, nonce);

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers["X-Frame-Options"] = "DENY";
            headers["Content-Security-Policy"] = BuildContentSecurityPolicy(nonce);
            return Task.CompletedTask;
        });

        await _next(context);
    }

    private static string BuildContentSecurityPolicy(string nonce) => string.Join("; ",
        "default-src 'self'",
        $"script-src 'self' 'nonce-{nonce}'",
        "script-src-attr 'none'",
        // MudBlazor uses inline styles for runtime layout; this allowance does not permit inline scripts.
        "style-src 'self' 'unsafe-inline'",
        "img-src 'self' data:",
        "font-src 'self'",
        "connect-src 'self'",
        "object-src 'none'",
        "base-uri 'self'",
        "form-action 'self'",
        "frame-ancestors 'none'",
        "frame-src 'none'");
}

/// <summary>Registers Relio's response security headers middleware.</summary>
public static class SecurityHeadersMiddlewareExtensions
{
    /// <summary>
    /// Adds the nonce-based Content Security Policy and baseline security headers to every response.
    /// Register this before exception handling, redirects, authentication, and endpoint middleware.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder.</returns>
    public static IApplicationBuilder UseRelioSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
