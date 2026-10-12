namespace Relio.Data.IntegrationTests.RestoreDrill;

/// <summary>
/// Always-on tests of the drill's own plumbing: when the phases run and what the canary looks like.
/// They need no database and no environment variables.
/// </summary>
public sealed class RestoreDrillSupportTests
{
    private static Func<string, string?> Environment(params (string Name, string Value)[] variables)
    {
        var map = variables.ToDictionary(variable => variable.Name, variable => variable.Value);
        return name => map.GetValueOrDefault(name);
    }

    private static (string, string)[] AllVariables(string phase) =>
    [
        (RestoreDrillEnvironment.Phase, phase),
        (RestoreDrillEnvironment.Connection, "Server=x"),
        (RestoreDrillEnvironment.KeyRingPath, "/keys"),
        (RestoreDrillEnvironment.CertificatePath, "/certificate.pfx"),
        (RestoreDrillEnvironment.CertificatePassword, "not-a-real-password"),
        (RestoreDrillEnvironment.Canary, "abcdefghijklmnop1234"),
        (RestoreDrillEnvironment.EvidencePath, "/evidence.json"),
    ];

    [Fact]
    public void Drill_phases_are_skipped_when_no_phase_is_set()
    {
        RestoreDrillEnvironment.GetSkipReason(RestoreDrillPhase.Seed, Environment())
            .Should().Contain(RestoreDrillEnvironment.Phase);
        RestoreDrillEnvironment.GetSkipReason(RestoreDrillPhase.Verify, Environment())
            .Should().Contain(RestoreDrillEnvironment.Phase);
    }

    [Fact]
    public void A_phase_runs_only_in_its_own_phase()
    {
        var verifyEnvironment = Environment(AllVariables("Verify"));
        var seedEnvironment = Environment(AllVariables("seed"));

        RestoreDrillEnvironment.GetSkipReason(RestoreDrillPhase.Seed, verifyEnvironment).Should().NotBeNull();
        RestoreDrillEnvironment.GetSkipReason(RestoreDrillPhase.Verify, verifyEnvironment).Should().BeNull();
        RestoreDrillEnvironment.GetSkipReason(RestoreDrillPhase.Seed, seedEnvironment).Should().BeNull("the phase name is case-insensitive");
        RestoreDrillEnvironment.GetSkipReason(RestoreDrillPhase.Verify, seedEnvironment).Should().NotBeNull();
    }

    [Fact]
    public void A_phase_with_a_missing_variable_is_skipped_and_names_only_the_variable()
    {
        var variables = AllVariables("Verify").Where(variable => variable.Item1 != RestoreDrillEnvironment.CertificatePassword);

        var reason = RestoreDrillEnvironment.GetSkipReason(RestoreDrillPhase.Verify, Environment(variables.ToArray()));

        reason.Should().Contain(RestoreDrillEnvironment.CertificatePassword);
        reason.Should().NotContain("not-a-real-password");
    }

    [Fact]
    public void The_skip_reason_never_contains_a_value()
    {
        var variables = AllVariables("Verify");

        var reason = RestoreDrillEnvironment.GetSkipReason(RestoreDrillPhase.Seed, Environment(variables));

        foreach (var (_, value) in variables.Where(variable => variable.Item1 != RestoreDrillEnvironment.Phase))
        {
            reason.Should().NotContain(value);
        }
    }

    [Fact]
    public void The_canary_is_deterministic_and_unique_per_run()
    {
        var first = RestoreDrillCanary.Create("abcdefghijklmnop1234");
        var again = RestoreDrillCanary.Create("abcdefghijklmnop1234");
        var other = RestoreDrillCanary.Create("zyxwvutsrqponmlk9876");

        again.OwnerId.Should().Be(first.OwnerId);
        again.HowWeMet.Should().Be(first.HowWeMet);
        other.OwnerId.Should().NotBe(first.OwnerId);
        other.NoteText.Should().NotBe(first.NoteText);
    }

    [Fact]
    public void Every_protected_value_carries_the_canary_and_is_distinct()
    {
        var canary = RestoreDrillCanary.Create("abcdefghijklmnop1234");

        canary.ProtectedValues.Should().OnlyContain(value => value.Contains(canary.Canary));
        canary.ProtectedValues.Should().OnlyHaveUniqueItems();
        canary.ProtectedValues.Should().HaveCount(11);
        canary.Email.Should().EndWith("@example.com");
        canary.ReminderTitle.Length.Should().BeLessThanOrEqualTo(200, "the reminder title has a column limit");
    }

    [Theory]
    [InlineData("short")]
    [InlineData("has spaces in it 1234567")]
    [InlineData("semi;colon-and-more-12345")]
    [InlineData("")]
    public void A_malformed_canary_is_rejected(string value)
    {
        var create = () => RestoreDrillCanary.Create(value);

        create.Should().Throw<ArgumentException>();
    }
}
