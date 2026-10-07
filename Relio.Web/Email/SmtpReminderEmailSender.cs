using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relio.Application.Reminders;

namespace Relio.Web.Email;

/// <summary>
/// The <see cref="IReminderEmailSender"/> used when <c>Email:Provider</c> is
/// <see cref="EmailOptions.SmtpProvider"/> (Issue #40).
/// Sends reminder emails over SMTP using <see cref="SmtpClient"/>.
/// In accordance with privacy and GDPR rules, the emails and logs contain NO notes or extraneous personal data.
/// </summary>
public sealed class SmtpReminderEmailSender : IReminderEmailSender
{
    private readonly IOptions<EmailOptions> _options;
    private readonly ILogger<SmtpReminderEmailSender> _logger;
    private readonly Func<MailMessage, CancellationToken, Task>? _customSendAsync;

    public SmtpReminderEmailSender(
        IOptions<EmailOptions> options,
        ILogger<SmtpReminderEmailSender> logger)
        : this(options, logger, null)
    {
    }

    public SmtpReminderEmailSender(
        IOptions<EmailOptions> options,
        ILogger<SmtpReminderEmailSender> logger,
        Func<MailMessage, CancellationToken, Task>? customSendAsync)
    {
        _options = options;
        _logger = logger;
        _customSendAsync = customSendAsync;
    }

    /// <inheritdoc />
    public Task SendImmediateReminderAsync(
        string toEmail,
        string personName,
        string reminderTitle,
        DateOnly dueDate,
        string? unsubscribeToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(personName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderTitle);

        var subject = $"Reminder: {reminderTitle} - {personName}";
        var body = BuildImmediateBody(personName, reminderTitle, dueDate, unsubscribeToken);
        return SendAsync(toEmail, subject, body, "immediate", cancellationToken);
    }

    /// <inheritdoc />
    public Task SendDailyDigestAsync(
        string toEmail,
        IReadOnlyList<DigestReminderItem> items,
        string? unsubscribeToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toEmail);
        ArgumentNullException.ThrowIfNull(items);

        var subject = "Relio: Daily reminder digest";
        var body = BuildDigestBody(items, unsubscribeToken);
        return SendAsync(toEmail, subject, body, "daily digest", cancellationToken);
    }

    public static string BuildImmediateBody(
        string personName,
        string reminderTitle,
        DateOnly dueDate,
        string? unsubscribeToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Reminder: {reminderTitle}");
        sb.AppendLine($"Person: {personName}");
        sb.AppendLine($"Due date: {dueDate:yyyy-MM-dd}");

        if (!string.IsNullOrWhiteSpace(unsubscribeToken))
        {
            sb.AppendLine();
            sb.AppendLine($"Unsubscribe: /unsubscribe?token={Uri.EscapeDataString(unsubscribeToken)}");
        }

        return sb.ToString();
    }

    public static string BuildDigestBody(
        IReadOnlyList<DigestReminderItem> items,
        string? unsubscribeToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Daily reminder digest:");
        sb.AppendLine();

        foreach (var item in items)
        {
            sb.AppendLine($"- {item.PersonName}: {item.Title} (Due: {item.DueDate:yyyy-MM-dd})");
        }

        if (!string.IsNullOrWhiteSpace(unsubscribeToken))
        {
            sb.AppendLine();
            sb.AppendLine($"Unsubscribe: /unsubscribe?token={Uri.EscapeDataString(unsubscribeToken)}");
        }

        return sb.ToString();
    }

    private async Task SendAsync(
        string toAddress,
        string subject,
        string body,
        string kind,
        CancellationToken cancellationToken)
    {
        var smtp = _options.Value.Smtp;

        using var message = new MailMessage
        {
            From = new MailAddress(smtp.FromAddress, smtp.FromName),
            Subject = subject,
            Body = body,
        };
        message.To.Add(toAddress);

        // Never log the recipient email address, reminder content, or tokens (GDPR privacy rule)
        _logger.LogInformation("Sending reminder email ({EmailKind}) via SMTP.", kind);

        if (_customSendAsync is not null)
        {
            await _customSendAsync(message, cancellationToken);
            return;
        }

        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.EnableSsl,
            Credentials = string.IsNullOrEmpty(smtp.Username)
                ? null
                : new NetworkCredential(smtp.Username, smtp.Password),
        };

        await client.SendMailAsync(message, cancellationToken);
    }
}
