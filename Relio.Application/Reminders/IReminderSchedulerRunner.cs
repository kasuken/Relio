namespace Relio.Application.Reminders;

/// <summary>
/// Background job runner that finds and delivers due reminders per user time zone (epic #36, issue #39).
/// </summary>
public interface IReminderSchedulerRunner
{
    /// <summary>
    /// Executes a single pass of the due reminders delivery job across all active users.
    /// Returns the number of reminders delivered.
    /// </summary>
    Task<int> RunDueRemindersJobAsync(CancellationToken cancellationToken = default);
}
