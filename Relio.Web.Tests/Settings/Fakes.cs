using Relio.Application.Accounts;
using Relio.Application.Profile;
using Relio.Application.Time;
using Relio.Domain;
using Relio.Web.Time;

namespace Relio.Web.Tests.Settings;

/// <summary>In-memory <see cref="IUserTimeZoneService"/> that validates ids like the real one and records what was saved.</summary>
internal sealed class FakeUserTimeZoneService(string initialTimeZoneId = "UTC") : IUserTimeZoneService
{
    public string TimeZoneId { get; private set; } = initialTimeZoneId;

    public List<string> Saved { get; } = [];

    public Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(TimeZoneIds.Parse(TimeZoneId));

    public Task<DateOnly> GetTodayAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DateOnly.FromDateTime(DateTime.UnixEpoch));

    public Task SetTimeZoneAsync(string ianaTimeZoneId, CancellationToken cancellationToken = default)
    {
        var timeZone = TimeZoneIds.Parse(ianaTimeZoneId);
        TimeZoneId = timeZone.Id;
        Saved.Add(timeZone.Id);
        return Task.CompletedTask;
    }

    public Task<bool> IsDueTodayAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<bool> IsOverdueAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

/// <summary>An <see cref="IBrowserTimeZoneReader"/> that reports a fixed value (or <see langword="null"/>).</summary>
internal sealed class FakeBrowserTimeZoneReader(string? browserTimeZoneId) : IBrowserTimeZoneReader
{
    public Task<string?> GetBrowserTimeZoneIdAsync() => Task.FromResult(browserTimeZoneId);
}

/// <summary>
/// In-memory <see cref="IUserProfileService"/> that applies the same trim and length rule as the
/// real one and records what was saved.
/// </summary>
internal sealed class FakeUserProfileService(string? initialDisplayName = null) : IUserProfileService
{
    public string? DisplayName { get; private set; } = initialDisplayName;

    public List<string?> Saved { get; } = [];

    public Task<string?> GetDisplayNameAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DisplayName);

    public Task SetDisplayNameAsync(string? displayName, CancellationToken cancellationToken = default)
    {
        var normalized = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        if (normalized is { Length: > UserProfile.DisplayNameMaxLength })
        {
            throw new ArgumentException("Too long.", nameof(displayName));
        }

        DisplayName = normalized;
        Saved.Add(normalized);
        return Task.CompletedTask;
    }
}

/// <summary>An <see cref="ITwoFactorStatusService"/> that reports whatever a test sets.</summary>
internal sealed class FakeTwoFactorStatusService(bool isEnabled = false, int recoveryCodesLeft = 0) : ITwoFactorStatusService
{
    public TwoFactorStatus Status { get; set; } = new(isEnabled, recoveryCodesLeft);

    public Task<TwoFactorStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Status);
}
