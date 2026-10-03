using Microsoft.Extensions.Configuration;
using Relio.Web.Identity;

namespace Relio.Web.Tests.Identity;

/// <summary>
/// Covers how <c>Email:Provider</c> selects whether Identity requires a confirmed account -
/// see <see cref="ServiceCollectionExtensions.AddRelioIdentity"/>.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void RequiresConfirmedAccount_is_false_when_Email_Provider_is_not_set()
    {
        var configuration = BuildConfiguration([]);

        ServiceCollectionExtensions.RequiresConfirmedAccount(configuration).Should().BeFalse();
    }

    [Theory]
    [InlineData("None")]
    [InlineData("none")]
    [InlineData("Unknown")]
    public void RequiresConfirmedAccount_is_false_for_none_or_unknown_providers(string provider)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?> { ["Email:Provider"] = provider });

        ServiceCollectionExtensions.RequiresConfirmedAccount(configuration).Should().BeFalse();
    }

    [Theory]
    [InlineData("Smtp")]
    [InlineData("smtp")]
    [InlineData("SMTP")]
    public void RequiresConfirmedAccount_is_true_for_smtp_regardless_of_casing(string provider)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?> { ["Email:Provider"] = provider });

        ServiceCollectionExtensions.RequiresConfirmedAccount(configuration).Should().BeTrue();
    }

    [Fact]
    public void BuildAccountOptions_defaults_match_the_issue_16_acceptance_criteria_when_unset()
    {
        var configuration = BuildConfiguration([]);

        var options = ServiceCollectionExtensions.BuildAccountOptions(configuration);

        options.Lockout.MaxFailedAccessAttempts.Should().Be(5);
        options.Lockout.DefaultLockoutTimeSpan.Should().Be(TimeSpan.FromMinutes(15));
        options.Lockout.AllowedForNewUsers.Should().BeTrue();
        options.Cookie.ExpireTimeSpan.Should().Be(TimeSpan.FromDays(14));
    }

    [Fact]
    public void BuildAccountOptions_reads_overridden_values()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Account:Lockout:MaxFailedAccessAttempts"] = "3",
            ["Account:Lockout:DefaultLockoutTimeSpan"] = "00:05:00",
            ["Account:Lockout:AllowedForNewUsers"] = "false",
            ["Account:Cookie:ExpireTimeSpan"] = "1.00:00:00",
        });

        var options = ServiceCollectionExtensions.BuildAccountOptions(configuration);

        options.Lockout.MaxFailedAccessAttempts.Should().Be(3);
        options.Lockout.DefaultLockoutTimeSpan.Should().Be(TimeSpan.FromMinutes(5));
        options.Lockout.AllowedForNewUsers.Should().BeFalse();
        options.Cookie.ExpireTimeSpan.Should().Be(TimeSpan.FromDays(1));
    }

    [Fact]
    public void BuildAccountOptions_applies_defaults_to_properties_left_out_of_a_partial_override()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Account:Lockout:MaxFailedAccessAttempts"] = "10",
        });

        var options = ServiceCollectionExtensions.BuildAccountOptions(configuration);

        options.Lockout.MaxFailedAccessAttempts.Should().Be(10);
        options.Lockout.DefaultLockoutTimeSpan.Should().Be(TimeSpan.FromMinutes(15));
        options.Lockout.AllowedForNewUsers.Should().BeTrue();
        options.Cookie.ExpireTimeSpan.Should().Be(TimeSpan.FromDays(14));
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
