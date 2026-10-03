using Relio.Web.Email;

namespace Relio.Web.Tests.Email;

public class EmailOptionsTests
{
    [Fact]
    public void Defaults_to_no_email_provider()
    {
        var options = new EmailOptions();

        options.Provider.Should().Be(EmailOptions.NoneProvider);
    }

    [Fact]
    public void Smtp_defaults_are_reasonable_for_submission_over_TLS()
    {
        var smtp = new SmtpOptions();

        smtp.Port.Should().Be(587);
        smtp.EnableSsl.Should().BeTrue();
        smtp.FromName.Should().Be("Relio");
    }
}
