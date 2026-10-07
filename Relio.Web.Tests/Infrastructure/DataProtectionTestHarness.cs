using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Relio.Web.Tests.Infrastructure;

internal static class DataProtectionTestHarness
{
    private static readonly Lazy<ProtectedTestKeyRing> Shared = new(ProtectedTestKeyRing.Create);

    static DataProtectionTestHarness() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (Shared.IsValueCreated)
            {
                Shared.Value.Dispose();
            }
        };

    public static void ConfigureDataProtection(IServiceCollection services) =>
        Shared.Value.ConfigureDataProtection(services);

    public static IDisposable ConfigureHostEnvironment() =>
        Shared.Value.ConfigureHostEnvironment();
}

internal sealed class ProtectedTestKeyRing : IDisposable
{
    private readonly string _rootDirectory;
    private readonly string _keyDirectory;
    private readonly string _certificatePath;
    private readonly string _certificatePassword;
    private readonly X509Certificate2 _certificate;

    private ProtectedTestKeyRing(
        string rootDirectory,
        string keyDirectory,
        string certificatePath,
        string certificatePassword)
    {
        _rootDirectory = rootDirectory;
        _keyDirectory = keyDirectory;
        _certificatePath = certificatePath;
        _certificatePassword = certificatePassword;
        _certificate = X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            certificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);
    }

    public void ConfigureDataProtection(IServiceCollection services) =>
        services.AddDataProtection()
            .SetApplicationName("Relio.Web.Tests")
            .PersistKeysToFileSystem(new DirectoryInfo(_keyDirectory))
            .ProtectKeysWithCertificate(_certificate)
            .UnprotectKeysWithAnyCertificate(_certificate);

    public IDisposable ConfigureHostEnvironment() => new HostEnvironmentScope(
        new Dictionary<string, string>
        {
            ["DataProtection__ApplicationName"] = "Relio.Web.Tests",
            ["DataProtection__KeyRingPath"] = _keyDirectory,
            ["DataProtection__ProtectionMode"] = "Certificate",
            ["DataProtection__Certificate__Path"] = _certificatePath,
            ["DataProtection__Certificate__Password"] = _certificatePassword,
        });

    public static ProtectedTestKeyRing Create()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        var rootDirectory = Path.Combine(
            localApplicationData,
            "Relio",
            "TestKeyRings",
            Guid.NewGuid().ToString("N"));
        var keyDirectory = Path.Combine(rootDirectory, "key-ring");
        var certificatePath = Path.Combine(rootDirectory, "protection-certificate.pfx");
        var certificatePassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Directory.CreateDirectory(keyDirectory);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Relio web test data protection certificate",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero));
        File.WriteAllBytes(
            certificatePath,
            certificate.Export(X509ContentType.Pkcs12, certificatePassword));

        return new ProtectedTestKeyRing(rootDirectory, keyDirectory, certificatePath, certificatePassword);
    }

    public void Dispose()
    {
        _certificate.Dispose();
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private sealed class HostEnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues;
        private readonly IReadOnlyDictionary<string, string> _testValues;

        public HostEnvironmentScope(IReadOnlyDictionary<string, string> testValues)
        {
            _testValues = testValues;
            _originalValues = Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .Where(entry => ((string)entry.Key).StartsWith("DataProtection__", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value);

            foreach (var name in _originalValues.Keys)
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            foreach (var (name, value) in _testValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var name in _testValues.Keys)
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            foreach (var (name, value) in _originalValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
