namespace Relio.Web.Encryption;

/// <summary>Validates durable, external Data Protection configuration without exposing secret values.</summary>
public static class RelioDataProtectionOptionsValidator
{
    /// <summary>Returns configuration errors using setting names only.</summary>
    public static IReadOnlyList<string> Validate(RelioDataProtectionOptions options, string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ApplicationName))
        {
            errors.Add("DataProtection:ApplicationName is required.");
        }

        ValidateExternalAbsolutePath(
            options.KeyRingPath,
            contentRootPath,
            "DataProtection:KeyRingPath",
            errors);

        if (string.Equals(options.ProtectionMode, "Dpapi", StringComparison.OrdinalIgnoreCase))
        {
            if (!OperatingSystem.IsWindows())
            {
                errors.Add("DataProtection:ProtectionMode=Dpapi is supported only on Windows.");
            }
        }
        else if (string.Equals(options.ProtectionMode, "Certificate", StringComparison.OrdinalIgnoreCase))
        {
            if (options.Certificate is null)
            {
                errors.Add("DataProtection:Certificate is required for certificate key protection.");
            }
            else
            {
                ValidateCertificate(options.Certificate, "DataProtection:Certificate", contentRootPath, errors);
            }

            var previousCertificates = options.PreviousCertificates ?? [];
            for (var index = 0; index < previousCertificates.Count; index++)
            {
                ValidateCertificate(
                    previousCertificates[index],
                    $"DataProtection:PreviousCertificates:{index}",
                    contentRootPath,
                    errors);
            }
        }
        else
        {
            errors.Add("DataProtection:ProtectionMode must be Dpapi or Certificate.");
        }

        return errors;
    }

    /// <summary>Finds the current repository root, when running from a source checkout.</summary>
    public static string GetRepositoryRoot(string contentRootPath)
    {
        DirectoryInfo? current = new(Path.GetFullPath(contentRootPath));
        while (current is not null)
        {
            var gitPath = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(contentRootPath);
    }

    private static void ValidateCertificate(
        DataProtectionCertificateOptions certificate,
        string settingPrefix,
        string contentRootPath,
        ICollection<string> errors)
    {
        if (certificate is null)
        {
            errors.Add($"{settingPrefix} is invalid.");
            return;
        }

        ValidateExternalAbsolutePath(
            certificate.Path,
            contentRootPath,
            $"{settingPrefix}:Path",
            errors);
        if (string.IsNullOrWhiteSpace(certificate.Password))
        {
            errors.Add($"{settingPrefix}:Password must be supplied by an external secret provider.");
        }
    }

    private static void ValidateExternalAbsolutePath(
        string? path,
        string contentRootPath,
        string settingName,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            errors.Add($"{settingName} must be an absolute external path.");
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            errors.Add($"{settingName} must be a valid absolute path.");
            return;
        }

        var repositoryRoot = GetRepositoryRoot(contentRootPath);
        if (IsWithin(fullPath, repositoryRoot))
        {
            errors.Add($"{settingName} must be outside the application repository.");
        }
    }

    private static bool IsWithin(string candidatePath, string parentPath)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(parentPath), Path.GetFullPath(candidatePath));
        return relative == "."
            || (!Path.IsPathRooted(relative)
                && relative != ".."
                && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
