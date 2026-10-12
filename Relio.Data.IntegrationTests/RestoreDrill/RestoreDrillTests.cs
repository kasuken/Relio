using Relio.Data.Encryption;
using Relio.Data.IntegrationTests.Infrastructure;

namespace Relio.Data.IntegrationTests.RestoreDrill;

/// <summary>
/// The two data phases of the restore drill (<c>deploy/restore-drill/drill.sh</c>, run by
/// <c>.github/workflows/restore-drill.yml</c>). Every test here is skipped in an ordinary run: it
/// only runs when the script sets <c>RELIO_RESTORE_DRILL_PHASE</c> and the connection, key ring and
/// canary of the instance under test (see <see cref="RestoreDrillEnvironment"/>).
/// </summary>
/// <remarks>
/// Assertions compare booleans or counts and never print a value: the test results are uploaded as
/// evidence and must not contain the canary, a password or a key. Run in one class so xUnit keeps
/// the tests of a phase sequential (they share the evidence file).
/// </remarks>
[Trait("Category", "RestoreDrill")]
public sealed class RestoreDrillTests
{
    [RestoreDrillFact(RestoreDrillPhase.Seed)]
    public async Task Seed_writes_synthetic_protected_rows()
    {
        var canary = RestoreDrillCanary.FromEnvironment();
        using var keyRing = RestoreDrillKeyRing.LoadFromEnvironment();

        var evidence = await RestoreDrillData.SeedAsync(
            RestoreDrillEnvironment.Required(RestoreDrillEnvironment.Connection),
            keyRing.FieldProtector,
            canary);

        evidence.RowCounts["People"].Should().Be(2);
        evidence.RowCounts["DifficultMoments"].Should().Be(1);
        evidence.LatestMigrationId.Should().NotBeEmpty("the source database was migrated by the application");
        evidence.Write(RestoreDrillEnvironment.Required(RestoreDrillEnvironment.EvidencePath));
    }

    [RestoreDrillFact(RestoreDrillPhase.Verify)]
    public async Task Restored_database_decrypts_every_protected_row()
    {
        using var keyRing = RestoreDrillKeyRing.LoadFromEnvironment();
        var service = new SensitiveFieldBackfillService(
            RestoreDrillData.CreateOptions(RestoreDrillEnvironment.Required(RestoreDrillEnvironment.Connection)),
            TimeProvider.System,
            keyRing.FieldProtector);

        // RunAsync authenticates every protected column of every row with the restored key ring
        // and throws if one cannot be read. Nothing may be converted: every row was already
        // protected when the backup was taken.
        var result = await service.RunAsync();

        result.Should().Be(new SensitiveFieldBackfillResult(0, 0, 0, 0, 0, 0));
        Update(evidence => evidence.BackfillConvertedTotal =
            result.People + result.Notes + result.Interactions + result.Reminders + result.UserProfiles + result.IdentityTokens);
    }

    [RestoreDrillFact(RestoreDrillPhase.Verify)]
    public async Task Restored_canary_matches_exact_plaintext()
    {
        var canary = RestoreDrillCanary.FromEnvironment();
        using var keyRing = RestoreDrillKeyRing.LoadFromEnvironment();

        var verified = await RestoreDrillData.VerifyCanaryAsync(
            RestoreDrillEnvironment.Required(RestoreDrillEnvironment.Connection),
            keyRing.FieldProtector,
            canary);

        (verified == canary.ProtectedValues.Count).Should().BeTrue(
            "every restored protected column must decrypt to exactly the value seeded before the backup");
        Update(evidence => evidence.CanaryFieldsVerified = verified);
    }

    [RestoreDrillFact(RestoreDrillPhase.Verify)]
    public async Task Restored_row_counts_match_the_backup_point()
    {
        using var keyRing = RestoreDrillKeyRing.LoadFromEnvironment();
        var connection = RestoreDrillEnvironment.Required(RestoreDrillEnvironment.Connection);
        var evidence = RestoreDrillEvidence.Read(RestoreDrillEnvironment.Required(RestoreDrillEnvironment.EvidencePath));

        var restored = await RestoreDrillData.CountRowsAsync(connection, keyRing.FieldProtector);
        var migration = await RestoreDrillData.ReadLatestMigrationAsync(connection);

        restored.Should().BeEquivalentTo(evidence.RowCounts);
        (migration == evidence.LatestMigrationId).Should().BeTrue(
            "the restored database must be at the schema version the backup was taken at");
        Update(updated =>
        {
            updated.RestoredRowCounts = restored;
            updated.VerifiedUtc = DateTimeOffset.UtcNow;
        });
    }

    [RestoreDrillFact(RestoreDrillPhase.Verify)]
    public async Task An_unrelated_key_ring_cannot_read_the_restored_data()
    {
        // Same application name, different keys: what a restore without the original key material
        // would look like. The data must stay unreadable (fail closed), not silently fall back.
        using var unrelated = DataProtectionTestHarness.CreateIsolatedKeyRing(RestoreDrillEnvironment.GetApplicationName());
        var service = new SensitiveFieldBackfillService(
            RestoreDrillData.CreateOptions(RestoreDrillEnvironment.Required(RestoreDrillEnvironment.Connection)),
            TimeProvider.System,
            unrelated.FieldProtector);

        var run = () => service.RunAsync();

        await run.Should().ThrowAsync<SensitiveFieldBackfillException>();
        Update(evidence => evidence.UnrelatedKeyRingRejected = true);
    }

    private static void Update(Action<RestoreDrillEvidence> change)
    {
        var path = RestoreDrillEnvironment.Required(RestoreDrillEnvironment.EvidencePath);
        var evidence = File.Exists(path) ? RestoreDrillEvidence.Read(path) : new RestoreDrillEvidence();
        change(evidence);
        evidence.Write(path);
    }
}
