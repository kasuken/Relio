using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Relio.Data.Encryption;

namespace Relio.Data.IntegrationTests.RestoreDrill;

/// <summary>
/// The Data Protection key ring and wrapping certificate of a Relio instance, opened from files
/// (a copy of <c>/var/opt/relio</c>) exactly the way the application opens them.
/// </summary>
/// <remarks>
/// Mirrors <c>Relio.Web.Encryption.RelioDataProtectionServiceCollectionExtensions</c> (stable
/// application name, file-system key ring, certificate protection, ephemeral key set); keep the two
/// in sync. This project cannot reference Relio.Web.
/// <para>
/// Unlike the application it never generates keys (<see cref="KeyManagementOptions.AutoGenerateKeys"/>
/// is off). A drill that quietly minted a missing key would make a broken restore look healthy, so a
/// ring without the restored keys must fail instead.
/// </para>
/// </remarks>
internal sealed class RestoreDrillKeyRing : IDisposable
{
    private readonly X509Certificate2 _certificate;
    private readonly ServiceProvider _services;

    private RestoreDrillKeyRing(string keyDirectory, string certificatePath, string certificatePassword, string applicationName)
    {
        _certificate = X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            certificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection()
            .SetApplicationName(applicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
            .ProtectKeysWithCertificate(_certificate)
            .UnprotectKeysWithAnyCertificate(_certificate);
        services.Configure<KeyManagementOptions>(options => options.AutoGenerateKeys = false);
        _services = services.BuildServiceProvider();
        FieldProtector = new DataProtectionFieldProtector(_services.GetRequiredService<IDataProtectionProvider>());
    }

    public IDataProtectionFieldProtector FieldProtector { get; }

    public static RestoreDrillKeyRing LoadFromEnvironment() =>
        new(
            RestoreDrillEnvironment.Required(RestoreDrillEnvironment.KeyRingPath),
            RestoreDrillEnvironment.Required(RestoreDrillEnvironment.CertificatePath),
            RestoreDrillEnvironment.Required(RestoreDrillEnvironment.CertificatePassword),
            RestoreDrillEnvironment.GetApplicationName());

    public void Dispose()
    {
        _services.Dispose();
        _certificate.Dispose();
    }
}
