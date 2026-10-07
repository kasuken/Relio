using Relio.Domain;

namespace Relio.Application.Reminders;

/// <summary>
/// Pure recurrence calculations for recurring reminders.
/// </summary>
public static class ReminderRecurrence
{
    /// <summary>
    /// Computes the next scheduled occurrence following <paramref name="currentDueDate"/>.
    /// When <paramref name="today"/> is supplied and the naive step falls in the past,
    /// advances occurrences until it lands strictly on or after today.
    /// </summary>
    public static DateOnly ComputeNextDueDate(
        DateOnly currentDueDate,
        ReminderFrequency frequency,
        int? customIntervalMonths = null,
        DateOnly? today = null)
    {
        Func<DateOnly, DateOnly> step = frequency switch
        {
            ReminderFrequency.Weekly => d => d.AddDays(7),
            ReminderFrequency.Monthly => d => d.AddMonths(1),
            ReminderFrequency.EveryThreeMonths => d => d.AddMonths(3),
            ReminderFrequency.EverySixMonths => d => d.AddMonths(6),
            ReminderFrequency.Yearly => d => d.AddYears(1),
            ReminderFrequency.CustomMonths => d => d.AddMonths(Math.Max(1, customIntervalMonths ?? 1)),
            _ => throw new ArgumentException($"Cannot compute next occurrence for frequency {frequency}", nameof(frequency)),
        };

        var next = step(currentDueDate);
        if (today is { } current && next < current)
        {
            while (next < current)
            {
                next = step(next);
            }
        }

        return next;
    }
}
