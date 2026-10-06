namespace Relio.Web.Time;

/// <summary>
/// Reads the signed-in browser's IANA time zone id (e.g. <c>"Europe/Rome"</c>) via
/// <c>Intl.DateTimeFormat().resolvedOptions().timeZone</c>, so account settings (#18) can suggest it as
/// the user's <c>Relio.Application.Time.IUserTimeZoneService</c> time zone instead of UTC (sign-up, #15,
/// reads the same value from a plain script instead - see Register.razor). Web only -
/// Application/Data services never depend on this; they always read the stored time zone.
/// </summary>
public interface IBrowserTimeZoneReader
{
    /// <summary>
    /// Returns the browser's resolved IANA time zone id, or <see langword="null"/> if the browser
    /// does not support <c>Intl.DateTimeFormat</c> (rare) or the id it reports is not a time zone
    /// .NET recognises - callers should fall back to
    /// <c>Relio.Application.Time.TimeZoneIds.Default</c> ("UTC") in that case.
    /// </summary>
    Task<string?> GetBrowserTimeZoneIdAsync();
}
