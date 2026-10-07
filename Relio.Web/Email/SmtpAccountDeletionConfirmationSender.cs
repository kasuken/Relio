using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Relio.Web.Email;

/// <summary>Sends the account-erasure notice through the configured SMTP provider.</summary>
public sealed class SmtpAccountDeletionConfirmationSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpAccountDeletionConfirmationSender> logger) : IAccountDeletionConfirmationSender
{
    /// <inheritdoc />
    public bool IsAvailable => options.Value.CanSendEmail;

    /// <inheritdoc />
    public async Task SendAsync(string address, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        var smtp = options.Value.Smtp;
        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.EnableSsl,
            Credentials = string.IsNullOrEmpty(smtp.Username)
                ? null
                : new NetworkCredential(smtp.Username, smtp.Password),
        };

        using var message = new MailMessage
        {
            From = new MailAddress(smtp.FromAddress, smtp.FromName),
            Subject = "Your Relio account was deleted",
            Body =
                "Your Relio account has been permanently deleted from the live database. " +
                "Backups are managed by the instance operator according to their retention policy. " +
                "If you did not request this, contact the person who runs this Relio instance.",
        };
        message.To.Add(address);

        // The recipient and body are never logged; addresses are personal data.
        logger.LogInformation("Sending an account-deletion confirmation via SMTP.");
        await client.SendMailAsync(message, cancellationToken);
    }
}
