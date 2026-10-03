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
}
