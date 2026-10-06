namespace Relio.Application.Time;

/// <summary>
/// Validates and resolves IANA time zone ids (e.g. <c>"Europe/Rome"</c>,
/// <c>"Pacific/Kiritimati"</c>). .NET on both Linux and Windows resolves IANA ids via ICU, so no
/// Windows-id mapping is needed. See the "Dates and time zones" section of AGENTS.md.
/// </summary>
public static class TimeZoneIds
{
    /// <summary>The time zone every user starts with until they set one at sign-up (#15) or change it in account settings (#18).</summary>
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

    private static readonly Lazy<IReadOnlyList<string>> AvailableIds = new(BuildAvailableIds);

    /// <summary>
    /// The IANA time zone ids Relio offers in time zone pickers (account settings, #18): every
    /// system time zone that <see cref="TryParse"/> accepts, plus <see cref="Default"/>, distinct
    /// and sorted ordinally. Computed once and cached.
    /// </summary>
    /// <remarks>
    /// On Linux and macOS this is every IANA zone the system knows. On Windows, system zones are
    /// Windows ids; each is mapped to a single IANA id with
    /// <see cref="TimeZoneInfo.TryConvertWindowsIdToIanaId(string, out string?)"/>, so the list is
    /// a representative subset (e.g. <c>Europe/Berlin</c> stands in for the whole "W. Europe
    /// Standard Time" zone, and <c>Europe/Rome</c> is not listed). It is a convenience for
    /// pickers, not the set of valid ids: <see cref="TryParse"/> remains the source of truth, so
    /// callers must still accept any id it accepts (a typed value, or one the browser reports).
    /// </remarks>
    public static IReadOnlyList<string> GetAvailableIds() => AvailableIds.Value;

    private static IReadOnlyList<string> BuildAvailableIds()
    {
        var ids = new List<string> { Default };

        foreach (var systemTimeZone in TimeZoneInfo.GetSystemTimeZones())
        {
            string? ianaId = null;
            if (systemTimeZone.HasIanaId)
            {
                ianaId = systemTimeZone.Id;
            }
            else if (TimeZoneInfo.TryConvertWindowsIdToIanaId(systemTimeZone.Id, out var converted))
            {
                ianaId = converted;
            }

            if (ianaId is not null && TryParse(ianaId, out _))
            {
                ids.Add(ianaId);
            }
        }

        return ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
}
