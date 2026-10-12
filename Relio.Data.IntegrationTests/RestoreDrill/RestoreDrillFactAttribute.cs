namespace Relio.Data.IntegrationTests.RestoreDrill;

/// <summary>
/// A <see cref="FactAttribute"/> for one phase of the restore drill. It is skipped (not failed, not
/// silently passed) unless <c>deploy/restore-drill/drill.sh</c> has set the phase and every
/// variable that phase needs, so ordinary test runs report these tests as skipped.
/// </summary>
/// <remarks>
/// xUnit 2.9 cannot decide a skip at runtime, so the reason is set in the constructor, the same way
/// <c>SqlServerFactAttribute</c> does it.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RestoreDrillFactAttribute : FactAttribute
{
    public RestoreDrillFactAttribute(RestoreDrillPhase phase)
    {
        Skip = RestoreDrillEnvironment.GetSkipReason(phase);
    }
}
