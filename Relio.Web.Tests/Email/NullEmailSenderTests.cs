using Microsoft.Extensions.Logging.Abstractions;
using Relio.Data.Identity;
using Relio.Web.Email;

namespace Relio.Web.Tests.Email;

/// <summary>
/// <see cref="NullEmailSender"/> is what Email:Provider=None wires up (the default - see
/// AGENTS.md's "Email features degrade gracefully when no email provider is configured"
/// guardrail): it must never throw, and must never send anything.
/// </summary>
public class NullEmailSenderTests
{
    private readonly NullEmailSender _sender = new(NullLogger<NullEmailSender>.Instance);
    private readonly RelioUser _user = new() { Email = "person@example.com" };

    [Fact]
    public async Task SendConfirmationLinkAsync_does_not_throw()
    {
        var act = () => _sender.SendConfirmationLinkAsync(_user, _user.Email!, "https://example.com/confirm");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendPasswordResetLinkAsync_does_not_throw()
    {
        var act = () => _sender.SendPasswordResetLinkAsync(_user, _user.Email!, "https://example.com/reset");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendPasswordResetCodeAsync_does_not_throw()
    {
        var act = () => _sender.SendPasswordResetCodeAsync(_user, _user.Email!, "123456");

        await act.Should().NotThrowAsync();
    }
}
