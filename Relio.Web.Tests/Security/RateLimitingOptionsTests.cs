using System.Globalization;
using Microsoft.Extensions.Options;
using Relio.Web.Security;

namespace Relio.Web.Tests.Security;

public sealed class RateLimitingOptionsTests
{
    [Fact]
    public void Defaults_enable_authentication_limits_with_no_queued_requests()
    {
        var options = new RateLimitingOptions();

        options.Enabled.Should().BeTrue();
        RateLimitingOptions.QueueLimit.Should().Be(0);
        options.LoginPermitLimit.Should().Be(10);
        options.LoginWindow.Should().Be(TimeSpan.FromMinutes(1));
        options.RegistrationPermitLimit.Should().Be(5);
        options.RegistrationWindow.Should().Be(TimeSpan.FromMinutes(10));
        options.PasswordResetPermitLimit.Should().Be(5);
        options.PasswordResetWindow.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Login_limit_must_be_positive(int permitLimit)
    {
        var options = new RateLimitingOptions { LoginPermitLimit = permitLimit };

        AssertInvalid(options, nameof(options.LoginPermitLimit));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Registration_limit_must_be_positive(int permitLimit)
    {
        var options = new RateLimitingOptions { RegistrationPermitLimit = permitLimit };

        AssertInvalid(options, nameof(options.RegistrationPermitLimit));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Password_reset_limit_must_be_positive(int permitLimit)
    {
        var options = new RateLimitingOptions { PasswordResetPermitLimit = permitLimit };

        AssertInvalid(options, nameof(options.PasswordResetPermitLimit));
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    [InlineData("30.00:00:01")]
    public void Login_window_must_be_positive_and_at_most_30_days(string window)
    {
        var options = new RateLimitingOptions
        {
            LoginWindow = TimeSpan.Parse(window, CultureInfo.InvariantCulture),
        };

        AssertInvalid(options, nameof(options.LoginWindow));
    }

    [Fact]
    public void Disabled_limits_are_still_validated()
    {
        var options = new RateLimitingOptions
        {
            Enabled = false,
            LoginPermitLimit = 0,
        };

        AssertInvalid(options, nameof(options.LoginPermitLimit));
    }

    private static void AssertInvalid(RateLimitingOptions options, string expectedFailure)
    {
        var result = new RateLimitingOptionsValidator().Validate(Options.DefaultName, options);

        result.Failed.Should().BeTrue();
        string.Join(Environment.NewLine, result.Failures
            ?? throw new InvalidOperationException("The failed validation did not report any failures."))
            .Should().Contain(expectedFailure);
    }
}
