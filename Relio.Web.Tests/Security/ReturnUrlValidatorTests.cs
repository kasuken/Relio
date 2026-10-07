using Relio.Web.Security;

namespace Relio.Web.Tests.Security;

/// <summary>
/// Covers issue #16's "ReturnUrl=https://evil.example is ignored" acceptance criterion - see
/// <see cref="ReturnUrlValidator"/>.
/// </summary>
public class ReturnUrlValidatorTests
{
    private const string BaseUri = "http://127.0.0.1:5000/";

    [Fact]
    public void Default_destination_is_the_protected_workspace()
    {
        ReturnUrlValidator.FallbackPath.Should().Be("/dashboard");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_or_blank_return_url_falls_back_to_the_default(string? returnUrl)
    {
        ReturnUrlValidator.GetSafeReturnUrl(returnUrl, BaseUri).Should().Be(ReturnUrlValidator.FallbackPath);
    }

    [Theory]
    [InlineData("/people")]
    [InlineData("/people?tab=notes")]
    [InlineData("/")]
    public void A_local_relative_path_is_returned_unchanged(string returnUrl)
    {
        ReturnUrlValidator.GetSafeReturnUrl(returnUrl, BaseUri).Should().Be(returnUrl);
    }

    [Fact]
    public void A_same_origin_absolute_url_is_reduced_to_a_path()
    {
        ReturnUrlValidator.GetSafeReturnUrl("http://127.0.0.1:5000/people", BaseUri).Should().Be("/people");
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("https://evil.example/people")]
    [InlineData("http://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("/\\/evil.example")]
    [InlineData("\\\\evil.example")]
    [InlineData("http://127.0.0.1:5000@evil.example")]
    [InlineData("javascript:alert(1)")]
    public void An_open_redirect_falls_back_to_the_default(string returnUrl)
    {
        ReturnUrlValidator.GetSafeReturnUrl(returnUrl, BaseUri).Should().Be(ReturnUrlValidator.FallbackPath);
    }

    [Fact]
    public void A_different_port_on_the_same_host_falls_back_to_the_default()
    {
        ReturnUrlValidator.GetSafeReturnUrl("http://127.0.0.1:9999/people", BaseUri)
            .Should().Be(ReturnUrlValidator.FallbackPath);
    }

    [Fact]
    public void A_malformed_base_uri_falls_back_to_the_default()
    {
        ReturnUrlValidator.GetSafeReturnUrl("/people", "not a uri").Should().Be(ReturnUrlValidator.FallbackPath);
    }
}
