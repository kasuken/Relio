using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Relio.Data.Identity;

namespace Relio.Web.E2ETests.Infrastructure;

/// <summary>
/// An <see cref="IEmailSender{TUser}"/> that captures account emails in memory instead of sending
/// them, so <see cref="EmailConfirmationTests"/> can follow the confirmation link without a real
/// mailbox. Registered in place of <c>Relio.Web.Email.SmtpEmailSender</c> via
/// <c>ConfigureTestServices</c> - see <see cref="RelioWebAppFixture"/>'s/<see cref="RelioWebAppFactory"/>'s
/// remarks on building a variant factory.
/// </summary>
public sealed class TestEmailSink : IEmailSender<RelioUser>
{
    private readonly ConcurrentQueue<string> _confirmationLinks = new();
    private readonly ConcurrentQueue<string> _passwordResetLinks = new();

    /// <inheritdoc />
    public Task SendConfirmationLinkAsync(RelioUser user, string email, string confirmationLink)
    {
        _confirmationLinks.Enqueue(confirmationLink);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendPasswordResetLinkAsync(RelioUser user, string email, string resetLink)
    {
        _passwordResetLinks.Enqueue(resetLink);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendPasswordResetCodeAsync(RelioUser user, string email, string resetCode) => Task.CompletedTask;

    /// <summary>The most recently captured confirmation link, or null if none was sent yet.</summary>
    public string? LastConfirmationLink => _confirmationLinks.IsEmpty ? null : _confirmationLinks.Last();

    /// <summary>
    /// The most recently captured password reset link, or null if none was sent yet (issue #17) -
    /// e.g. because the submitted email did not match any account.
    /// </summary>
    public string? LastPasswordResetLink => _passwordResetLinks.IsEmpty ? null : _passwordResetLinks.Last();
}
