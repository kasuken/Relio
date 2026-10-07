using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Relio.Web.Configuration;

namespace Relio.Web.Tests.Marketing;

public sealed class MarketingOptionsValidationTests
{
    [Fact]
    public void Source_code_link_has_a_safe_default_and_is_required_to_be_https_outside_development()
    {
        var defaults = new SourceCodeOptions();
        defaults.SourceCodeUrl.Should().Be(SourceCodeOptions.DefaultSourceCodeUrl);

        var validator = new SourceCodeOptionsValidator(CreateEnvironment(Environments.Production));
        validator.Validate(Options.DefaultName, defaults).Succeeded.Should().BeTrue();
        validator.Validate(
                Options.DefaultName,
                new SourceCodeOptions { SourceCodeUrl = "http://source.example/repo" })
            .Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://username:password@source.example/repo")]
    [InlineData("https://@source.example/repo")]
    [InlineData("https://user@source.example/repo")]
    [InlineData("https://source.example/repo?token=secret")]
    [InlineData("https://source.example/repo#fragment")]
    public void Source_code_link_rejects_unsafe_urls_without_echoing_the_value(string url)
    {
        var validator = new SourceCodeOptionsValidator(CreateEnvironment(Environments.Development));

        var result = validator.Validate(
            Options.DefaultName,
            new SourceCodeOptions { SourceCodeUrl = url });

        result.Failed.Should().BeTrue();
        DescribeFailures(result).Should().NotContain(url);
    }

    [Fact]
    public void Seo_origin_is_optional_when_not_hosting_policies()
    {
        var validator = new SeoOptionsValidator(CreateEnvironment(Environments.Development));

        validator.Validate(Options.DefaultName, new SeoOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Seo_head_canonical_paths_are_application_owned_and_use_the_configured_origin()
    {
        var options = new SeoOptions { PublicOrigin = "https://relio.example" };

        options.GetCanonicalUrl("/about").Should().Be("https://relio.example/about");
        options.GetCanonicalUrl(PolicyDocumentKind.Privacy).Should().Be("https://relio.example/privacy");
    }

    [Theory]
    [InlineData("//attacker.example/path")]
    [InlineData("/privacy?token=private")]
    [InlineData("/privacy#fragment")]
    [InlineData("/../private")]
    [InlineData("/privacy\\..\\private")]
    public void Seo_canonical_path_rejects_nonlocal_or_dynamic_paths(string path)
    {
        var options = new SeoOptions { PublicOrigin = "https://relio.example" };

        Action buildCanonicalUrl = () => options.GetCanonicalUrl(path);

        buildCanonicalUrl.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("relio.example")]
    [InlineData("https://relio.example/path")]
    [InlineData("https://relio.example?token=secret")]
    [InlineData("javascript:alert(1)")]
    public void Seo_origin_rejects_non_origin_or_unsafe_values(string origin)
    {
        var validator = new SeoOptionsValidator(CreateEnvironment(Environments.Production));

        var result = validator.Validate(
            Options.DefaultName,
            new SeoOptions { PublicOrigin = origin });

        result.Failed.Should().BeTrue();
        DescribeFailures(result).Should().NotContain(origin);
    }

    [Fact]
    public void Policy_hosting_is_off_without_requiring_an_origin_or_document_content()
    {
        var validator = CreateHostedPoliciesValidator(CreateEnvironment(Environments.Development), new SeoOptions());

        validator.Validate(Options.DefaultName, new HostedPoliciesOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Enabling_policy_hosting_requires_an_origin_and_at_least_one_attested_document()
    {
        var validator = CreateHostedPoliciesValidator(CreateEnvironment(Environments.Production), new SeoOptions());

        var result = validator.Validate(
            Options.DefaultName,
            new HostedPoliciesOptions { Enabled = true });

        result.Failed.Should().BeTrue();
        var failures = DescribeFailures(result);
        failures.Should().Contain("Seo:PublicOrigin");
        failures.Should().Contain("must enable at least one document");
    }

    [Fact]
    public void Enabled_documents_require_unique_metadata_external_content_and_a_utc_review_attestation()
    {
        var environment = CreateEnvironment(Environments.Production);
        var validator = CreateHostedPoliciesValidator(
            environment,
            new SeoOptions { PublicOrigin = "https://relio.example" });
        var options = CreateCompleteOptions();

        validator.Validate(Options.DefaultName, options).Succeeded.Should().BeTrue();

        options.Terms.Title = options.Privacy.Title;
        var duplicateTitle = validator.Validate(Options.DefaultName, options);
        duplicateTitle.Failed.Should().BeTrue();
        DescribeFailures(duplicateTitle)
            .Should().Contain("Title must be unique");

        options.Terms.Title = "Terms test fixture";
        options.Privacy.HumanReviewAttested = false;
        var missingReviewAttestation = validator.Validate(Options.DefaultName, options);
        missingReviewAttestation.Failed.Should().BeTrue();
        DescribeFailures(missingReviewAttestation)
            .Should().Contain("HumanReviewAttested must be true only after a human has reviewed");

        options.Privacy.HumanReviewAttested = true;
        options.Privacy.ReviewedAtUtc = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.FromHours(2));
        var nonUtcReview = validator.Validate(Options.DefaultName, options);
        nonUtcReview.Failed.Should().BeTrue();
        DescribeFailures(nonUtcReview)
            .Should().Contain("ReviewedAtUtc must be supplied in UTC");
    }

    [Fact]
    public void Enabled_policy_content_must_be_outside_the_application_content_root()
    {
        var environment = CreateEnvironment(Environments.Production);
        var validator = CreateHostedPoliciesValidator(
            environment,
            new SeoOptions { PublicOrigin = "https://relio.example" });
        var options = CreateCompleteOptions();
        options.Privacy.ContentFile = Path.Combine(environment.ContentRootPath, "privacy.txt");

        var result = validator.Validate(Options.DefaultName, options);

        result.Failed.Should().BeTrue();
        DescribeFailures(result)
            .Should().Contain("ContentFile must be an absolute path outside the application content root");
    }

    private static string DescribeFailures(ValidateOptionsResult result) =>
        string.Join(Environment.NewLine, result.Failures
            ?? throw new InvalidOperationException("The failed validation did not report any failures."));

    private static HostedPoliciesOptionsValidator CreateHostedPoliciesValidator(
        IHostEnvironment environment,
        SeoOptions seoOptions) =>
        new(Options.Create(seoOptions), environment);

    private static HostedPoliciesOptions CreateCompleteOptions()
    {
        var root = Path.GetPathRoot(Environment.CurrentDirectory)
            ?? throw new InvalidOperationException("The test environment has no filesystem root.");

        return new HostedPoliciesOptions
        {
            Enabled = true,
            Privacy = CreateDocument(root, "privacy"),
            Terms = CreateDocument(root, "terms"),
            AcceptableUse = CreateDocument(root, "acceptable-use"),
        };
    }

    private static PolicyDocumentOptions CreateDocument(string root, string name) =>
        new()
        {
            Enabled = true,
            ContentFile = Path.Combine(root, "relio-policy-validation", $"{name}.txt"),
            Title = $"{name} policy test fixture",
            Description = $"Synthetic {name} policy test fixture; not a real policy.",
            Version = "test-1",
            HumanReviewAttested = true,
            ReviewedAtUtc = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero),
        };

    private static IHostEnvironment CreateEnvironment(string name)
    {
        var contentRootPath = Path.Combine(Environment.CurrentDirectory, "marketing-test-content-root");
        return new TestHostEnvironment
        {
            EnvironmentName = name,
            ApplicationName = typeof(MarketingOptionsValidationTests).Assembly.GetName().Name!,
            ContentRootPath = contentRootPath,
            ContentRootFileProvider = new NullFileProvider(),
        };
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = string.Empty;

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
