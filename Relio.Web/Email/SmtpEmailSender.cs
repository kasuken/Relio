using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Relio.Data.Identity;

namespace Relio.Web.Email;

/// <summary>
/// The <c>IEmailSender&lt;RelioUser&gt;</c> used when <c>Email:Provider</c> is
/// <see cref="EmailOptions.SmtpProvider"/>. Built on <see cref="System.Net.Mail.SmtpClient"/>
/// (part of the .NET base class library) rather than a third-party package such as MailKit:
/// Relio only needs to send a handful of plain transactional emails (confirmation, password
/// reset), which <see cref="SmtpClient"/> already does over TLS with basic auth, so adding a new
/// dependency - and its own vulnerability surface - is not justified for the MVP. Revisit if a
/// later feature needs something <see cref="SmtpClient"/> cannot do (OAuth2, DKIM signing, etc).
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    : IEmailSender<RelioUser>
{
    /// <inheritdoc />
    /// <remarks>Used both to confirm a new account (#15) and to confirm a changed email address (#18), so the wording stays neutral.</remarks>
    public Task SendConfirmationLinkAsync(RelioUser user, string email, string confirmationLink) =>
        SendAsync(email, "Confirm your email address for Relio", BuildLinkBody("Confirm your email address", confirmationLink));

    /// <inheritdoc />
    public Task SendPasswordResetLinkAsync(RelioUser user, string email, string resetLink) =>
        SendAsync(email, "Reset your Relio password", BuildLinkBody("Reset your password", resetLink));

    /// <inheritdoc />
    public Task SendPasswordResetCodeAsync(RelioUser user, string email, string resetCode) =>
        SendAsync(email, "Your Relio password reset code", $"Your password reset code is: {resetCode}");

    private static string BuildLinkBody(string action, string link) =>
        $"{action} by following this link: {link}\n\nIf you did not request this, you can ignore this email.";

    private async Task SendAsync(string toAddress, string subject, string body)
    {
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
            Subject = subject,
            Body = body,
        };
        message.To.Add(toAddress);

        // Never log the body: it carries the confirmation/reset link or code, a bearer
        // credential (see the gdpr-compliant skill) - only that an email was attempted.
        logger.LogInformation("Sending account email ({Subject}) via SMTP.", subject);

        await client.SendMailAsync(message);
    }
}
