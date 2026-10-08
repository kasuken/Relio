using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Relio.Web.Encryption;

namespace Relio.Web.Tests.Encryption;

public sealed class RelioDataProtectionOptionsValidatorTests
{
    [Fact]
    public void Validation_requires_explicit_durable_key_ring_configuration()
    {
        var errors = RelioDataProtectionOptionsValidator.Validate(
            new RelioDataProtectionOptions(),
            AppContext.BaseDirectory);

        errors.Should().Contain(error => error.Contains("ApplicationName", StringComparison.Ordinal));
        errors.Should().Contain(error => error.Contains("KeyRingPath", StringComparison.Ordinal));
        errors.Should().Contain(error => error.Contains("ProtectionMode", StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_rejects_key_and_certificate_paths_inside_the_source_repository()
    {
        var contentRoot = Environment.CurrentDirectory;
        var options = new RelioDataProtectionOptions
        {
            ApplicationName = "Relio",
            KeyRingPath = Path.Combine(contentRoot, "keys"),
            ProtectionMode = "Certificate",
            Certificate = new DataProtectionCertificateOptions
            {
                Path = Path.Combine(contentRoot, "certificate.pfx"),
                Password = "provided-by-test",
            },
        };

        var errors = RelioDataProtectionOptionsValidator.Validate(options, contentRoot);

        errors.Should().Contain(error => error.Contains("KeyRingPath", StringComparison.Ordinal));
        errors.Should().Contain(error => error.Contains("Certificate:Path", StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_rejects_certificates_without_an_external_password()
    {
        var options = new RelioDataProtectionOptions
        {
            ApplicationName = "Relio",
            KeyRingPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Relio",
                "production-keys"),
            ProtectionMode = "Certificate",
            Certificate = new DataProtectionCertificateOptions
            {
                Path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Relio",
                    "data-protection.pfx"),
            },
        };

        RelioDataProtectionOptionsValidator.Validate(options, AppContext.BaseDirectory)
            .Should().Contain(error => error.Contains("Password", StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_tolerates_a_null_previous_certificate_collection_from_configuration_binding()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var options = new RelioDataProtectionOptions
        {
            ApplicationName = "Relio",
            KeyRingPath = Path.Combine(localApplicationData, "Relio", "production-keys"),
            ProtectionMode = "Certificate",
            Certificate = new DataProtectionCertificateOptions
            {
                Path = Path.Combine(localApplicationData, "Relio", "data-protection.pfx"),
                Password = "provided-by-test",
            },
            PreviousCertificates = null!,
        };

        var validate = () => RelioDataProtectionOptionsValidator.Validate(options, AppContext.BaseDirectory);

        validate.Should().NotThrow();
    }

    [Fact]
    public void Registration_fails_fast_when_data_protection_is_not_configured()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        var act = () => services.AddRelioDataProtection(configuration, AppContext.BaseDirectory);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Data Protection configuration is invalid*");
    }

    [Fact]
    public void AutoGenerateIfMissing_generates_certificate_and_registers_data_protection()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Relio-DP-Auto-" + Guid.NewGuid().ToString("N"));
        var keyRingPath = Path.Combine(tempDir, "keys");
        var certPath = Path.Combine(tempDir, "auto-cert.pfx");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DataProtection:ApplicationName"] = "Relio.Test",
                    ["DataProtection:KeyRingPath"] = keyRingPath,
                    ["DataProtection:ProtectionMode"] = "Certificate",
                    ["DataProtection:Certificate:Path"] = certPath,
                    ["DataProtection:Certificate:Password"] = "auto-test-pass",
                    ["DataProtection:AutoGenerateIfMissing"] = "true",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddRelioDataProtection(configuration, AppContext.BaseDirectory);

            File.Exists(certPath).Should().BeTrue();
            Directory.Exists(keyRingPath).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
