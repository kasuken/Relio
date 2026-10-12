using Microsoft.Extensions.Logging;
using Relio.Web.Diagnostics;
using Relio.Web.Tests.Infrastructure;

namespace Relio.Web.Tests.Diagnostics;

/// <summary>
/// Pins the operational event catalogue. These ids are referenced by docs/operations/monitoring.md and
/// infra/modules/monitoring.bicep - changing them breaks operator alerts.
/// </summary>
public sealed class RelioOperationalEventsTests
{
    public static TheoryData<int, string, int> Catalogue => new()
    {
        { 0, "RelioUnhandledHttpFailure", 9001 },
        { 1, "RelioCircuitFailure", 9002 },
        { 2, "RelioReminderJobFailure", 9003 },
        { 3, "RelioProductMetricsRetentionFailure", 9004 },
        { 4, "RelioBackupFailure", 9005 },
    };

    [Theory]
    [MemberData(nameof(Catalogue))]
    public void Event_keeps_its_stable_id_name_and_level(int index, string expectedName, int expectedId)
    {
        var capture = new CapturingLoggerProvider();
        var logger = capture.CreateLogger("test");
        const string type = nameof(InvalidOperationException);

        switch (index)
        {
            case 0: RelioOperationalEvents.UnhandledHttpFailure(logger, type); break;
            case 1: RelioOperationalEvents.CircuitFailure(logger, type, "CircuitHost.X", "abc"); break;
            case 2: RelioOperationalEvents.ReminderJobFailure(logger, type); break;
            case 3: RelioOperationalEvents.ProductMetricsRetentionFailure(logger, type); break;
            case 4: RelioOperationalEvents.BackupFailure(logger, type); break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }

        var entry = capture.Entries.Should().ContainSingle().Subject;
        entry.EventId.Id.Should().Be(expectedId);
        entry.EventId.Name.Should().Be(expectedName);
        entry.Level.Should().Be(LogLevel.Error);
        entry.Exception.Should().BeNull();
        entry.StateText.Should().Contain($"ExceptionType={type}");
    }

    [Fact]
    public void Exception_type_is_none_without_an_exception()
    {
        RelioOperationalEvents.ExceptionTypeOf(null).Should().Be("None");
        RelioOperationalEvents.ExceptionTypeOf(new TimeoutException("secret")).Should().Be(nameof(TimeoutException));
    }
}
