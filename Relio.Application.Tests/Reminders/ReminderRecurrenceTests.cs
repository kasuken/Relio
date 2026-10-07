using Relio.Application.Reminders;
using Relio.Domain;

namespace Relio.Application.Tests.Reminders;

public class ReminderRecurrenceTests
{
    [Fact]
    public void Weekly_recurrence_adds_7_days()
    {
        var due = new DateOnly(2026, 10, 1);
        var next = ReminderRecurrence.ComputeNextDueDate(due, ReminderFrequency.Weekly);

        next.Should().Be(new DateOnly(2026, 10, 8));
    }

    [Fact]
    public void Monthly_recurrence_adds_1_month()
    {
        var due = new DateOnly(2026, 10, 1);
        var next = ReminderRecurrence.ComputeNextDueDate(due, ReminderFrequency.Monthly);

        next.Should().Be(new DateOnly(2026, 11, 1));
    }

    [Fact]
    public void Quarterly_recurrence_adds_3_months()
    {
        var due = new DateOnly(2026, 10, 1);
        var next = ReminderRecurrence.ComputeNextDueDate(due, ReminderFrequency.EveryThreeMonths);

        next.Should().Be(new DateOnly(2027, 1, 1));
    }

    [Fact]
    public void SemiAnnually_recurrence_adds_6_months()
    {
        var due = new DateOnly(2026, 10, 1);
        var next = ReminderRecurrence.ComputeNextDueDate(due, ReminderFrequency.EverySixMonths);

        next.Should().Be(new DateOnly(2027, 4, 1));
    }

    [Fact]
    public void Yearly_recurrence_adds_1_year()
    {
        var due = new DateOnly(2026, 10, 1);
        var next = ReminderRecurrence.ComputeNextDueDate(due, ReminderFrequency.Yearly);

        next.Should().Be(new DateOnly(2027, 10, 1));
    }

    [Fact]
    public void CustomMonths_recurrence_adds_custom_months()
    {
        var due = new DateOnly(2026, 10, 1);
        var next = ReminderRecurrence.ComputeNextDueDate(due, ReminderFrequency.CustomMonths, customIntervalMonths: 4);

        next.Should().Be(new DateOnly(2027, 2, 1));
    }

    [Fact]
    public void Recurrence_when_overdue_steps_forward_to_on_or_after_today()
    {
        var due = new DateOnly(2026, 8, 1);
        var today = new DateOnly(2026, 10, 7);

        var next = ReminderRecurrence.ComputeNextDueDate(due, ReminderFrequency.Monthly, today: today);

        next.Should().Be(new DateOnly(2026, 11, 1));
    }
}
