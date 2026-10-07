using System.Net.Mail;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Relio.Application.Reminders;
using Relio.Web.Email;

namespace Relio.Web.Tests.Email;

public class ReminderEmailSenderTests
{
    private readonly EmailOptions _options = new()
    {
        Provider = EmailOptions.SmtpProvider,
        Smtp = new SmtpOptions
        {
            Host = "smtp.example.com",
            Port = 587,
            FromAddress = "noreply@relio.app",
            FromName = "Relio",
        },
    };

    [Fact]
    public async Task SendImmediateReminderAsync_contains_only_person_title_and_due_date_with_unsubscribe_link()
    {
        MailMessage? capturedMessage = null;
        var sender = new SmtpReminderEmailSender(
            Options.Create(_options),
            NullLogger<SmtpReminderEmailSender>.Instance,
            (msg, _) =>
            {
                capturedMessage = msg;
                return Task.CompletedTask;
            });

        var dueDate = new DateOnly(2026, 11, 15);
        await sender.SendImmediateReminderAsync(
            "user@example.com",
            "Marta Rossi",
            "Call about the project",
            dueDate,
            "tok_abc123");

        capturedMessage.Should().NotBeNull();
        capturedMessage!.To.Should().ContainSingle(m => m.Address == "user@example.com");
        capturedMessage.From!.Address.Should().Be("noreply@relio.app");
        capturedMessage.Subject.Should().Contain("Call about the project");
        capturedMessage.Subject.Should().Contain("Marta Rossi");

        var body = capturedMessage.Body;
        body.Should().Contain("Person: Marta Rossi");
        body.Should().Contain("Reminder: Call about the project");
        body.Should().Contain("Due date: 2026-11-15");
        body.Should().Contain("/unsubscribe?token=tok_abc123");

        // CRITICAL INVARIANT: No notes or other personal details
        body.Should().NotContain("Note:");
        body.Should().NotContain("Notes");
    }

    [Fact]
    public async Task SendImmediateReminderAsync_omits_unsubscribe_link_when_token_is_null()
    {
        MailMessage? capturedMessage = null;
        var sender = new SmtpReminderEmailSender(
            Options.Create(_options),
            NullLogger<SmtpReminderEmailSender>.Instance,
            (msg, _) =>
            {
                capturedMessage = msg;
                return Task.CompletedTask;
            });

        await sender.SendImmediateReminderAsync(
            "user@example.com",
            "Marta Rossi",
            "Catch up",
            new DateOnly(2026, 11, 15),
            unsubscribeToken: null);

        capturedMessage.Should().NotBeNull();
        capturedMessage!.Body.Should().NotContain("/unsubscribe");
    }

    [Fact]
    public async Task SendDailyDigestAsync_contains_all_items_with_person_title_due_date_and_unsubscribe_link()
    {
        MailMessage? capturedMessage = null;
        var sender = new SmtpReminderEmailSender(
            Options.Create(_options),
            NullLogger<SmtpReminderEmailSender>.Instance,
            (msg, _) =>
            {
                capturedMessage = msg;
                return Task.CompletedTask;
            });

        var items = new List<DigestReminderItem>
        {
            new("Alice", "Buy gift", new DateOnly(2026, 10, 10)),
            new("Bob", "Schedule coffee", new DateOnly(2026, 10, 10)),
        };

        await sender.SendDailyDigestAsync(
            "user@example.com",
            items,
            "tok_digest456");

        capturedMessage.Should().NotBeNull();
        capturedMessage!.To.Should().ContainSingle(m => m.Address == "user@example.com");

        var body = capturedMessage.Body;
        body.Should().Contain("Alice");
        body.Should().Contain("Buy gift");
        body.Should().Contain("Bob");
        body.Should().Contain("Schedule coffee");
        body.Should().Contain("2026-10-10");
        body.Should().Contain("/unsubscribe?token=tok_digest456");

        // CRITICAL INVARIANT: No notes
        body.Should().NotContain("Note:");
        body.Should().NotContain("Notes");
    }

    [Fact]
    public async Task NullReminderEmailSender_does_not_throw()
    {
        var sender = new NullReminderEmailSender(NullLogger<NullReminderEmailSender>.Instance);

        var actImmediate = () => sender.SendImmediateReminderAsync(
            "user@example.com",
            "Alice",
            "Reminder",
            new DateOnly(2026, 10, 10),
            "token");

        var actDigest = () => sender.SendDailyDigestAsync(
            "user@example.com",
            [new DigestReminderItem("Alice", "Reminder", new DateOnly(2026, 10, 10))],
            "token");

        await actImmediate.Should().NotThrowAsync();
        await actDigest.Should().NotThrowAsync();
    }
}
