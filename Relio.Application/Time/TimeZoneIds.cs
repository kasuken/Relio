namespace Relio.Application.Time;

/// <summary>
/// Validates and resolves IANA time zone ids (e.g. <c>"Europe/Rome"</c>,
/// <c>"Pacific/Kiritimati"</c>). .NET on both Linux and Windows resolves IANA ids via ICU, so no
/// Windows-id mapping is needed. See the "Dates and time zones" section of AGENTS.md.
/// </summary>
public static class TimeZoneIds
{
    /// <summary>The time zone every user starts with until they set one (sign-up, #15) or change it (settings, #18).</summary>
    public const string Default = "UTC";

    /// <summary>
    /// Attempts to resolve <paramref name="timeZoneId"/> to a <see cref="TimeZoneInfo"/>. Returns
    /// <see langword="false"/> for <see langword="null"/>, empty/whitespace, or any id
    /// <see cref="TimeZoneInfo.TryFindSystemTimeZoneById"/> does not recognise.
    /// </summary>
    public static bool TryParse(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            timeZone = null!;
            return false;
        }

        return TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out timeZone!);
    }

    /// <summary>
    /// Resolves <paramref name="timeZoneId"/>, throwing <see cref="InvalidTimeZoneIdException"/>
    /// instead of returning <see langword="false"/>. Use this on write paths (setting a user's
    /// time zone); use <see cref="TryParse"/> on read paths that should fall back instead of
    /// throwing.
    /// </summary>
    public static TimeZoneInfo Parse(string timeZoneId)
    {
        if (!TryParse(timeZoneId, out var timeZone))
        {
            throw new InvalidTimeZoneIdException(timeZoneId);
        }

        return timeZone;
    }
}
