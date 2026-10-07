namespace Relio.Application.Reminders;

/// <summary>
/// A single reminder entry within a daily digest email (Issue #40).
/// In accordance with privacy rules, contains ONLY the person's name, title, and due date.
/// </summary>
public sealed record DigestReminderItem(string PersonName, string Title, DateOnly DueDate);
