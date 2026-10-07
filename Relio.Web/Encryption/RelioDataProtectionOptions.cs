namespace Relio.Web.Encryption;

/// <summary>Configuration for the shared, durable ASP.NET Core Data Protection key ring.</summary>
public sealed class RelioDataProtectionOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "DataProtection";

    /// <summary>The stable application discriminator shared by every Relio instance using this data.</summary>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>An absolute, durable key-ring directory outside the application repository.</summary>
    public string KeyRingPath { get; set; } = string.Empty;

    /// <summary>Either <c>Dpapi</c> or <c>Certificate</c>.</summary>
    public string ProtectionMode { get; set; } = string.Empty;

    /// <summary>The active certificate configuration, required when <see cref="ProtectionMode"/> is <c>Certificate</c>.</summary>
    public DataProtectionCertificateOptions? Certificate { get; set; }

    /// <summary>
    /// Previous certificates whose private keys can decrypt existing key-ring entries during
    /// certificate rotation.
    /// </summary>
    public List<DataProtectionCertificateOptions> PreviousCertificates { get; set; } = [];
}

/// <summary>External PKCS#12 certificate configuration for protecting key-ring entries.</summary>
public sealed class DataProtectionCertificateOptions
{
    /// <summary>An absolute path to a PKCS#12 certificate file outside the application repository.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The external secret used to open the certificate file.</summary>
    public string Password { get; set; } = string.Empty;
}
