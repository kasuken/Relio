using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Relio.Data.Encryption;

namespace Relio.Web.Encryption;

/// <summary>Configures Relio's one durable Data Protection system for cookies and encrypted fields.</summary>
public static class RelioDataProtectionServiceCollectionExtensions
{
    /// <summary>
    /// Adds an external, durable and at-rest-protected key ring with a stable application name,
    /// then registers the field-protection and startup-validation services.
    /// </summary>
    public static IServiceCollection AddRelioDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        var options = configuration.GetSection(RelioDataProtectionOptions.SectionName)
            .Get<RelioDataProtectionOptions>() ?? new RelioDataProtectionOptions();
        var errors = RelioDataProtectionOptionsValidator.Validate(options, contentRootPath);
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(
                $"Relio Data Protection configuration is invalid: {string.Join(" ", errors)}");
        }

        var keyDirectory = Path.GetFullPath(options.KeyRingPath);
        EnsureWritableDirectory(keyDirectory);

        var builder = services.AddDataProtection()
            .SetApplicationName(options.ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));

        if (string.Equals(options.ProtectionMode, "Dpapi", StringComparison.OrdinalIgnoreCase))
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new InvalidOperationException("DPAPI key protection requires Windows.");
            }

            builder.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }
        else
        {
            var certificates = RelioDataProtectionCertificateSet.Load(options);
            services.AddSingleton(_ => certificates);
            builder.ProtectKeysWithCertificate(certificates.Current);
            builder.UnprotectKeysWithAnyCertificate(certificates.All.ToArray());
        }

        services.AddRelioFieldProtection();
        services.AddSingleton<IDataProtectionStartupCheck>(provider =>
        {
            _ = provider.GetService<RelioDataProtectionCertificateSet>();
            return new DataProtectionStartupCheck(provider.GetRequiredService<IDataProtectionProvider>());
        });
        return services;
    }

    private static void EnsureWritableDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probePath = Path.Combine(path, $".relio-key-ring-check-{Guid.NewGuid():N}");
            using (new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            throw new InvalidOperationException(
                "The configured Data Protection key-ring directory cannot be created or written.");
        }
    }
}

/// <summary>Holds the configured certificate chain for the host lifetime.</summary>
internal sealed class RelioDataProtectionCertificateSet : IDisposable
{
    private RelioDataProtectionCertificateSet(X509Certificate2 current, IReadOnlyList<X509Certificate2> previous)
    {
        Current = current;
        Previous = previous;
        All = new[] { current }.Concat(previous).ToArray();
    }

    public X509Certificate2 Current { get; }

    public IReadOnlyList<X509Certificate2> Previous { get; }

    public IReadOnlyList<X509Certificate2> All { get; }

    public static RelioDataProtectionCertificateSet Load(RelioDataProtectionOptions options)
    {
        var loaded = new List<X509Certificate2>();
        try
        {
            var current = LoadCertificate(options.Certificate!);
            loaded.Add(current);
            var previous = new List<X509Certificate2>();
            foreach (var certificateOptions in options.PreviousCertificates ?? [])
            {
                var certificate = LoadCertificate(certificateOptions);
                loaded.Add(certificate);
                previous.Add(certificate);
            }
            if (loaded.Any(certificate => !certificate.HasPrivateKey))
            {
                throw new InvalidOperationException();
            }

            return new RelioDataProtectionCertificateSet(current, previous);
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException)
        {
            foreach (var certificate in loaded)
            {
                certificate.Dispose();
            }

            throw new InvalidOperationException(
                "A configured Data Protection certificate is missing, inaccessible, or does not contain its private key.");
        }
    }

    public void Dispose()
    {
        foreach (var certificate in All)
        {
            certificate.Dispose();
        }
    }

    private static X509Certificate2 LoadCertificate(DataProtectionCertificateOptions options) =>
        X509CertificateLoader.LoadPkcs12FromFile(
            options.Path,
            options.Password,
            X509KeyStorageFlags.EphemeralKeySet);
}
