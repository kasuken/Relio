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

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
