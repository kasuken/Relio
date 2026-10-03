namespace Relio.Application.Time;

/// <summary>
/// Thrown when a caller supplies a time zone id that is not a known IANA time zone id (see
/// <see cref="TimeZoneIds.TryParse"/>). Deliberately rejected rather than silently falling back to
/// UTC, so a typo in a settings form or sign-up request surfaces immediately.
/// </summary>
public sealed class InvalidTimeZoneIdException(string timeZoneId)
    : ArgumentException($"'{timeZoneId}' is not a known IANA time zone id.", nameof(timeZoneId));
