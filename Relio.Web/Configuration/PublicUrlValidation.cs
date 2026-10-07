namespace Relio.Web.Configuration;

internal static class PublicUrlValidation
{
    public static bool IsSafeHttpUrl(string? value, bool requireHttps, bool originOnly = false)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.IsWellFormedOriginalString()
            || string.IsNullOrWhiteSpace(uri.Host)
            || uri.UserInfo.Length != 0
            || HasUserInfoDelimiter(value)
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0)
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (requireHttps && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !originOnly || string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal);
    }

    private static bool HasUserInfoDelimiter(string value)
    {
        var schemeSeparator = value.IndexOf("://", StringComparison.OrdinalIgnoreCase);
        if (schemeSeparator < 0)
        {
            return true;
        }

        var authorityStart = schemeSeparator + 3;
        var authorityEnd = value.IndexOfAny(['/', '?', '#'], authorityStart);
        if (authorityEnd < 0)
        {
            authorityEnd = value.Length;
        }

        return value.AsSpan(authorityStart, authorityEnd - authorityStart).IndexOf('@') >= 0;
    }
}
