using Microsoft.AspNetCore.Identity;
using Relio.Data.Identity;

namespace Relio.Web.Email;

/// <summary>
/// The <c>IEmailSender&lt;RelioUser&gt;</c> used when <c>Email:Provider</c> is
/// <see cref="EmailOptions.NoneProvider"/> (the default). Sends nothing - self-hosted Relio must
/// work with no email provider configured (epic #14's guardrails) - and logs once per call, at
/// warning level, so the gap is visible in the logs rather than silently swallowed. Never logs the
/// link/code itself: it is effectively a bearer credential (see the gdpr-compliant skill).
/// </summary>
public sealed class NullEmailSender(ILogger<NullEmailSender> logger) : IEmailSender<RelioUser>
{
    /// <inheritdoc />
    public Task SendConfirmationLinkAsync(RelioUser user, string email, string confirmationLink) =>
        LogSkippedAsync("confirmation link");

    /// <inheritdoc />
    public Task SendPasswordResetLinkAsync(RelioUser user, string email, string resetLink) =>
        LogSkippedAsync("password reset link");

    /// <inheritdoc />
    public Task SendPasswordResetCodeAsync(RelioUser user, string email, string resetCode) =>
        LogSkippedAsync("password reset code");

    private Task LogSkippedAsync(string kind)
    {
        logger.LogWarning(
            "Not sending {EmailKind}: Email:Provider is '{Provider}'. Set Email:Provider=Smtp " +
            "(and Email:Smtp:*) to enable account emails.",
            kind,
            EmailOptions.NoneProvider);
        return Task.CompletedTask;
    }
}
