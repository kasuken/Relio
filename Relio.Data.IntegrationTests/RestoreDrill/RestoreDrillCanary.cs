using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Relio.Data.IntegrationTests.RestoreDrill;

/// <summary>
/// The synthetic protected values the drill writes before the backup and checks, exactly, after the
/// restore. Every value contains the run's random canary, so "the restored value equals the expected
/// value" proves the right keys decrypted the right row, and "the raw column does not contain the
/// canary" proves the value was ciphertext at rest.
/// </summary>
/// <remarks>
/// A new protected column must get a value here, next to the column lists in
/// <c>SensitiveFieldBackfillService</c> (see "Backups and restore" in AGENTS.md). The values are
/// fictional; the canary is generated per run and is never a real secret.
/// </remarks>
internal sealed partial class RestoreDrillCanary
{
    private const int MinLength = 16;
    private const int MaxLength = 64;

    private RestoreDrillCanary(string canary)
    {
        Canary = canary;
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canary)))[..16];
        OwnerId = $"restore-drill-{digest}";
        Email = $"restore-drill-{digest}@example.com";
        HowWeMet = $"restore-drill {canary} how-we-met";
        PersonDetails = $"restore-drill {canary} person-details";
        NoteText = $"restore-drill {canary} note";
        InteractionDescription = $"restore-drill {canary} interaction";
        ReminderTitle = $"restore-drill {canary} reminder";
        DifficultMomentDescription = $"restore-drill {canary} difficult-moment-description";
        DifficultMomentTrigger = $"restore-drill {canary} difficult-moment-trigger";
        DifficultMomentResolution = $"restore-drill {canary} difficult-moment-resolution";
        DifficultMomentLessonsLearned = $"restore-drill {canary} difficult-moment-lessons";
        UnsubscribeToken = $"restore-drill-{canary}-unsubscribe";
        IdentityTokenValue = $"restore-drill-{canary}-authenticator";
    }

    public string Canary { get; }

    public string OwnerId { get; }

    public string Email { get; }

    public string HowWeMet { get; }

    public string PersonDetails { get; }

    public string NoteText { get; }

    public string InteractionDescription { get; }

    public string ReminderTitle { get; }

    public string DifficultMomentDescription { get; }

    public string DifficultMomentTrigger { get; }

    public string DifficultMomentResolution { get; }

    public string DifficultMomentLessonsLearned { get; }

    public string UnsubscribeToken { get; }

    public string IdentityTokenValue { get; }

    public static RestoreDrillCanary Create(string canary)
    {
        ArgumentNullException.ThrowIfNull(canary);
        if (canary.Length is < MinLength or > MaxLength || !CanaryPattern().IsMatch(canary))
        {
            throw new ArgumentException(
                $"The canary must be {MinLength} to {MaxLength} letters and digits.",
                nameof(canary));
        }

        return new RestoreDrillCanary(canary);
    }

    public static RestoreDrillCanary FromEnvironment() =>
        Create(RestoreDrillEnvironment.Required(RestoreDrillEnvironment.Canary));

    /// <summary>Every value that must be ciphertext in the database.</summary>
    public IReadOnlyList<string> ProtectedValues =>
    [
        HowWeMet,
        PersonDetails,
        NoteText,
        InteractionDescription,
        ReminderTitle,
        DifficultMomentDescription,
        DifficultMomentTrigger,
        DifficultMomentResolution,
        DifficultMomentLessonsLearned,
        UnsubscribeToken,
        IdentityTokenValue,
    ];

    [GeneratedRegex("^[A-Za-z0-9]+$")]
    private static partial Regex CanaryPattern();
}
