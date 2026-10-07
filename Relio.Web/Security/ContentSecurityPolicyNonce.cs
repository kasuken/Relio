using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Relio.Web.Security;

/// <summary>Provides the per-response nonce used by Relio's Content Security Policy.</summary>
public static class ContentSecurityPolicyNonce
{
    private const string HttpContextItemKey = "Relio.ContentSecurityPolicyNonce";

    /// <summary>Returns the nonce attached to the current HTTP response, if one is available.</summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>The response nonce, or <see langword="null"/> outside an HTTP request.</returns>
    public static string? Get(HttpContext? httpContext)
    {
        if (httpContext is null || !httpContext.Items.TryGetValue(HttpContextItemKey, out var value))
        {
            return null;
        }

        return value as string;
    }

    internal static string Create() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    internal static void Set(HttpContext httpContext, string nonce) =>
        httpContext.Items[HttpContextItemKey] = nonce;
}
