namespace Relio.Application.Time;

/// <summary>
/// Resolves the current user's time zone and calendar "today", and answers whether a calendar
/// date (reminder, birthday, interaction) is due today or overdue for them. Scoped to the
/// signed-in user (<c>ICurrentUser</c>), like every other Application service - see the
/// "User-scoped data pattern" section of AGENTS.md. Built on <see cref="UserCalendar"/> and an
/// injected <see cref="TimeProvider"/>, so date logic stays deterministic in tests. See the
/// "Dates and time zones" section of AGENTS.md.
/// </summary>
public interface IUserTimeZoneService
{
    /// <summary>
    /// The current user's time zone, or UTC (<see cref="TimeZoneIds.Default"/>) if they have not
    /// set one yet (e.g. before sign-up, #15, finishes creating their profile).
    /// </summary>
    Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default);

    /// <summary>Today's calendar date in the current user's time zone.</summary>
    Task<DateOnly> GetTodayAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the current user's time zone, creating their profile if it does not already exist.
    /// Used by sign-up (#15, defaulted from the browser) and account settings (#18); every other feature only reads.
    /// </summary>
    /// <exception cref="InvalidTimeZoneIdException">
    /// <paramref name="ianaTimeZoneId"/> is not a known IANA time zone id.
    /// </exception>
    Task SetTimeZoneAsync(string ianaTimeZoneId, CancellationToken cancellationToken = default);

    /// <summary>Whether <paramref name="date"/> is today, in the current user's time zone.</summary>
    Task<bool> IsDueTodayAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether <paramref name="date"/> is overdue (strictly before today), in the current user's
    /// time zone.
    /// </summary>
    Task<bool> IsOverdueAsync(DateOnly date, CancellationToken cancellationToken = default);
}
