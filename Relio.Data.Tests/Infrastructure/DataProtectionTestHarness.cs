using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Relio.Data.Encryption;

namespace Relio.Data.Tests.Infrastructure;

internal static class DataProtectionTestHarness
{
    private static readonly Lazy<EphemeralProtectedKeyRing> Shared = new(() => EphemeralProtectedKeyRing.Create());

    static DataProtectionTestHarness() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (Shared.IsValueCreated)
            {
                Shared.Value.Dispose();
            }
        };

    public static IDataProtectionFieldProtector FieldProtector => Shared.Value.FieldProtector;

    public static void ConfigureDataProtection(IServiceCollection services) =>
        Shared.Value.ConfigureDataProtection(services);

    public static EphemeralProtectedKeyRing CreateIsolatedKeyRing(string applicationName = "Relio.Data.Tests.Isolated") =>
        EphemeralProtectedKeyRing.Create(applicationName);
}

internal sealed class EphemeralProtectedKeyRing : IDisposable
{
    private readonly string _rootDirectory;
    private readonly string _keyDirectory;
    private readonly string _certificatePath;
    private readonly string _certificatePassword;
    private readonly string _applicationName;
    private readonly bool _deleteOnDispose;
    private readonly ServiceProvider _services;
    private readonly X509Certificate2 _certificate;

    private EphemeralProtectedKeyRing(
        string rootDirectory,
        string keyDirectory,
        string certificatePath,
        string certificatePassword,
        string applicationName,
        bool deleteOnDispose)
    {
        _rootDirectory = rootDirectory;
        _keyDirectory = keyDirectory;
        _certificatePath = certificatePath;
        _certificatePassword = certificatePassword;
        _applicationName = applicationName;
        _deleteOnDispose = deleteOnDispose;

        _certificate = X509CertificateLoader.LoadPkcs12FromFile(
            _certificatePath,
            _certificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);

        var services = new ServiceCollection();
        services.AddLogging();
        ConfigureDataProtection(services);
        _services = services.BuildServiceProvider();
        FieldProtector = new DataProtectionFieldProtector(
            _services.GetRequiredService<IDataProtectionProvider>());
    }

    public IDataProtectionFieldProtector FieldProtector { get; }

    public IDataProtectionProvider Provider => _services.GetRequiredService<IDataProtectionProvider>();

    public IKeyManager KeyManager => _services.GetRequiredService<IKeyManager>();

    public string KeyDirectory => _keyDirectory;

    public void ConfigureDataProtection(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddDataProtection()
            .SetApplicationName(_applicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(_keyDirectory))
            .ProtectKeysWithCertificate(_certificate)
            .UnprotectKeysWithAnyCertificate(_certificate);
    }

    public EphemeralProtectedKeyRing Restart() =>
        new(_rootDirectory, _keyDirectory, _certificatePath, _certificatePassword, _applicationName, deleteOnDispose: false);

    public static EphemeralProtectedKeyRing Create(string applicationName = "Relio.Data.Tests")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

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
            "CN=Relio data protection test certificate",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var generatedCertificate = request.CreateSelfSigned(
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero));
        File.WriteAllBytes(
            certificatePath,
            generatedCertificate.Export(X509ContentType.Pkcs12, certificatePassword));

        return new EphemeralProtectedKeyRing(
            rootDirectory,
            keyDirectory,
            certificatePath,
            certificatePassword,
            applicationName,
            deleteOnDispose: true);
    }

    public void Dispose()
    {
        _services.Dispose();
        _certificate.Dispose();

        if (_deleteOnDispose && Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}
