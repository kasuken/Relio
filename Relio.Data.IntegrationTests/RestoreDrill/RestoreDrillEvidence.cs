using System.Text.Json;

namespace Relio.Data.IntegrationTests.RestoreDrill;

/// <summary>
/// What the drill records about the data, as JSON that is uploaded as evidence. It holds row counts,
/// a schema version and timestamps only: never a name, a value, a key or a password.
/// </summary>
internal sealed class RestoreDrillEvidence
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public DateTimeOffset SeededUtc { get; set; }

    public string LatestMigrationId { get; set; } = string.Empty;

    public Dictionary<string, int> RowCounts { get; set; } = new();

    public DateTimeOffset? VerifiedUtc { get; set; }

    public Dictionary<string, int>? RestoredRowCounts { get; set; }

    /// <summary>Rows the verification had to convert: 0 means every restored row authenticated as already protected.</summary>
    public int? BackfillConvertedTotal { get; set; }

    public int? CanaryFieldsVerified { get; set; }

    public bool? UnrelatedKeyRingRejected { get; set; }

    public static RestoreDrillEvidence Read(string path) =>
        JsonSerializer.Deserialize<RestoreDrillEvidence>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("The restore drill evidence file is empty.");

    public void Write(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
