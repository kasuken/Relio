namespace Relio.Application.Reminders;

/// <summary>
/// User-scoped service for managing reconnect reminders (epic #36, issue #37).
/// </summary>
public interface IReminderService
{
    /// <summary>Gets a reminder by ID, or null if not found or not owned by the current user.</summary>
    Task<ReminderDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists reminders for the current user, optionally including completed ones, ordered by effective due date.</summary>
    Task<IReadOnlyList<ReminderDto>> ListAsync(bool includeCompleted = false, CancellationToken cancellationToken = default);

    /// <summary>Lists reminders for a specific person belonging to the current user.</summary>
    Task<IReadOnlyList<ReminderDto>> ListForPersonAsync(Guid personId, bool includeCompleted = false, CancellationToken cancellationToken = default);

    /// <summary>Lists active (not completed) reminders that are due on or before a given date in the user's calendar.</summary>
    Task<IReadOnlyList<ReminderDto>> ListDueAsync(DateOnly onOrBeforeDate, CancellationToken cancellationToken = default);

    /// <summary>Creates a new reconnect reminder for a person owned by the current user.</summary>
    Task<ReminderDto> CreateAsync(CreateReminderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing reminder owned by the current user.</summary>
    Task<ReminderDto?> UpdateAsync(Guid id, UpdateReminderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a reminder. For recurring reminders, calculates and schedules the next occurrence.
    /// For one-off reminders, marks them completed. Returns false if not found or not owned.
    /// </summary>
    Task<bool> CompleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Snoozes a reminder until a specific date. Returns false if not found or not owned.</summary>
    Task<bool> SnoozeAsync(Guid id, DateOnly snoozedUntilDate, CancellationToken cancellationToken = default);

    /// <summary>Deletes a reminder permanently. Returns false if not found or not owned.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
