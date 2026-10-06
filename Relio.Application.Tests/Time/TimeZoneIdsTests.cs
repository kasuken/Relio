using Relio.Application.Time;

namespace Relio.Application.Tests.Time;

public class TimeZoneIdsTests
{
    [Theory]
    [InlineData("UTC")]
    [InlineData("Europe/Rome")]
    [InlineData("America/New_York")]
    [InlineData("Pacific/Kiritimati")]
    [InlineData("Pacific/Pago_Pago")]
    public void TryParse_accepts_known_IANA_ids(string id)
    {
        var result = TimeZoneIds.TryParse(id, out var timeZone);

        result.Should().BeTrue();
        timeZone.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not/AZone")]
    [InlineData("Eastern Standard Time Nonsense")]
    public void TryParse_rejects_unknown_or_blank_ids(string? id)
    {
        TimeZoneIds.TryParse(id, out _).Should().BeFalse();
    }

    [Fact]
    public void Parse_returns_the_resolved_time_zone_for_a_known_id()
    {
        var timeZone = TimeZoneIds.Parse("Europe/Rome");

        timeZone.Id.Should().Be("Europe/Rome");
    }

    [Fact]
    public void Parse_throws_InvalidTimeZoneIdException_for_an_unknown_id()
    {
        var act = () => TimeZoneIds.Parse("Not/AZone");

        act.Should().Throw<InvalidTimeZoneIdException>()
            .WithMessage("*Not/AZone*");
    }

    [Fact]
    public void Default_is_UTC()
    {
        TimeZoneIds.Default.Should().Be("UTC");
    }

    [Fact]
    public void GetAvailableIds_includes_UTC()
    {
        TimeZoneIds.GetAvailableIds().Should().Contain("UTC");
    }

    [Fact]
    public void GetAvailableIds_includes_common_IANA_zones()
    {
        // Deliberately zones that map one-to-one on both Windows (via the CLDR mapping) and
        // Linux/macOS; not Europe/Rome, which a Windows machine only lists as Europe/Berlin - see
        // GetAvailableIds's remarks.
        TimeZoneIds.GetAvailableIds().Should().Contain(["America/New_York", "Asia/Tokyo"]);
    }

    [Fact]
    public void GetAvailableIds_only_contains_ids_that_TryParse_accepts()
    {
        foreach (var id in TimeZoneIds.GetAvailableIds())
        {
            TimeZoneIds.TryParse(id, out _).Should().BeTrue($"'{id}' is offered in the picker, so it must be savable");
        }
    }

    [Fact]
    public void GetAvailableIds_is_sorted_and_distinct()
    {
        var ids = TimeZoneIds.GetAvailableIds();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }
}
