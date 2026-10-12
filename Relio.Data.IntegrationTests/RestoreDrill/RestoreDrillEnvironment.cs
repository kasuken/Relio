namespace Relio.Data.IntegrationTests.RestoreDrill;

/// <summary>The two halves of the restore drill that run against a real, separately started database.</summary>
public enum RestoreDrillPhase
{
    /// <summary>Writes synthetic protected rows into the source instance before it is backed up.</summary>
    Seed,

    /// <summary>Verifies the instance that was restored from the encrypted backup alone.</summary>
    Verify,
}

/// <summary>
/// The environment contract between <c>deploy/restore-drill/drill.sh</c> and the drill tests. When
/// the phase variable is absent (every ordinary test run) the drill tests are skipped, so they cost
/// nothing and never fail a developer's run.
/// </summary>
/// <remarks>
/// Only variable <em>names</em> ever appear in a skip reason or a message. The values include a
/// certificate password and a canary and must not reach the uploaded test results.
/// </remarks>
internal static class RestoreDrillEnvironment
{
    public const string Phase = "RELIO_RESTORE_DRILL_PHASE";
    public const string Connection = "RELIO_RESTORE_DRILL_CONNECTION";
    public const string KeyRingPath = "RELIO_RESTORE_DRILL_KEY_RING_PATH";
    public const string CertificatePath = "RELIO_RESTORE_DRILL_CERTIFICATE_PATH";
    public const string CertificatePassword = "RELIO_RESTORE_DRILL_CERTIFICATE_PASSWORD";
    public const string ApplicationName = "RELIO_RESTORE_DRILL_APPLICATION_NAME";
    public const string Canary = "RELIO_RESTORE_DRILL_CANARY";
    public const string EvidencePath = "RELIO_RESTORE_DRILL_EVIDENCE_PATH";

    /// <summary>The application name the compose file gives the app (<c>DataProtection__ApplicationName</c>).</summary>
    public const string DefaultApplicationName = "Relio";

    private static readonly string[] RequiredVariables =
    [
        Connection,
        KeyRingPath,
        CertificatePath,
        CertificatePassword,
        Canary,
        EvidencePath,
    ];

    /// <summary>The reason a test for <paramref name="phase"/> must be skipped, or <see langword="null"/> to run it.</summary>
    public static string? GetSkipReason(RestoreDrillPhase phase, Func<string, string?> getVariable)
    {
        ArgumentNullException.ThrowIfNull(getVariable);

        var configuredPhase = getVariable(Phase);
        if (string.IsNullOrWhiteSpace(configuredPhase))
        {
            return $"{Phase} is not set: the restore drill phases only run from deploy/restore-drill/drill.sh.";
        }

        if (!string.Equals(configuredPhase, phase.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return $"This run is the '{configuredPhase}' phase of the restore drill, not '{phase}'.";
        }

        var missing = RequiredVariables.Where(name => string.IsNullOrWhiteSpace(getVariable(name))).ToArray();
        return missing.Length == 0
            ? null
            : $"The restore drill '{phase}' phase is missing: {string.Join(", ", missing)}.";
    }

    public static string? GetSkipReason(RestoreDrillPhase phase) =>
        GetSkipReason(phase, Environment.GetEnvironmentVariable);

    public static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} is not set.");

    public static string GetApplicationName() =>
        Environment.GetEnvironmentVariable(ApplicationName) is { Length: > 0 } value ? value : DefaultApplicationName;
}
