namespace Relio.Web.Background;

/// <summary>
/// Configuration options for <see cref="ReminderSchedulerBackgroundService"/> (epic #36, issue #39).
/// </summary>
public sealed class ReminderSchedulerOptions
{
    /// <summary>The configuration section name in appsettings (<c>"ReminderScheduler"</c>).</summary>
    public const string SectionName = "ReminderScheduler";

    /// <summary>
    /// Whether the background reminder scheduler is enabled. Defaults to <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often the background scheduler checks for due reminders. Defaults to 1 hour.
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(1);
}
