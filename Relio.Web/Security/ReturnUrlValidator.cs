namespace Relio.Web.Security;

/// <summary>
/// Validates a <c>returnUrl</c> query parameter (login, and anywhere else a "come back here after
/// signing in" link is built) against open redirect attacks - issue #16's "ReturnUrl=https://evil.example
/// is ignored" acceptance criterion. A pure function over <see cref="Uri"/> (no ASP.NET Core
/// dependency), so it is unit testable with no HTTP context at all.
/// </summary>
public static class ReturnUrlValidator
{
    /// <summary>Where an unsafe or missing return URL falls back to.</summary>
    public const string FallbackPath = "/dashboard";

    /// <summary>
    /// Returns <paramref name="returnUrl"/> reduced to a same-origin, path-only redirect target,
    /// or <see cref="FallbackPath"/> when it is missing, malformed, or points somewhere else
    /// (a different scheme, host or port than <paramref name="baseUri"/> - including a
    /// protocol-relative <c>//evil.example</c> value, which resolves to a different host under
    /// the same scheme).
    /// </summary>
    /// <param name="returnUrl">The untrusted value from the request (e.g. a query parameter).</param>
    /// <param name="baseUri">
    /// The app's own absolute base URI (e.g. <c>NavigationManager.BaseUri</c>), used both to
    /// resolve a relative <paramref name="returnUrl"/> and as the origin it is compared against.
    /// </param>
    public static string GetSafeReturnUrl(string? returnUrl, string baseUri)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return FallbackPath;
        }

        if (!Uri.TryCreate(baseUri, UriKind.Absolute, out var basedUri))
        {
            return FallbackPath;
        }

        // Uri's relative-resolution rules (RFC 3986) already do the hard part: a value starting
        // with "//" is a network-path reference that keeps the base's scheme but replaces the
        // host/port with whatever follows the slashes - exactly the protocol-relative open
        // redirect trick - so it naturally fails the origin comparison below, same as a fully
        // qualified "https://evil.example".
        if (!Uri.TryCreate(basedUri, returnUrl, out var resolved))
        {
            return FallbackPath;
        }

        var sameOrigin = Uri.Compare(
            resolved,
            basedUri,
            UriComponents.SchemeAndServer,
            UriFormat.UriEscaped,
            StringComparison.OrdinalIgnoreCase) == 0;

        if (!sameOrigin)
        {
            return FallbackPath;
        }

        // Strip the scheme/host back off even though it matched - NavigationManager.NavigateTo
        // never needs to see one, and a path-only result can't smuggle one back in later.
        var target = resolved.PathAndQuery + resolved.Fragment;

        if (string.IsNullOrEmpty(target))
        {
            return FallbackPath;
        }

        // A value like "/\evil.example" resolves same-origin by Uri's own rules (the lone leading
        // "/" makes it an absolute-path reference against the base authority, and "\" normalizes
        // to "/"), but Uri folds that into a path of "//evil.example" - two leading slashes. Uri
        // itself knows that string is still relative to this same origin, but the caller doesn't
        // hand this result back to Uri: it hands the bare string to a browser (NavigationManager,
        // a redirect Location header, an anchor href), which parses a leading "//" as
        // scheme-relative to whatever host follows - the open redirect this whole check exists to
        // stop. Reject rather than keep resolving it, instead of e.g. collapsing the slashes: the
        // only inputs that produce this shape are exactly these bypass attempts.
        return target.StartsWith("//", StringComparison.Ordinal) ? FallbackPath : target;
    }
}
