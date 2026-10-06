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

    [Theory]
    [InlineData("None", false)]
    [InlineData("Smtp", true)]
    [InlineData("smtp", true)]
    [InlineData("Carrier-pigeon", false)]
    [InlineData("", false)]
    public void CanSendEmail_is_true_only_for_the_Smtp_provider(string provider, bool expected)
    {
        var options = new EmailOptions { Provider = provider };

        options.CanSendEmail.Should().Be(expected);
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
