using Relio.Application.Reminders;
using Relio.Domain;

namespace Relio.Application.Tests.Reminders;

public class ReachOutCalculatorTests
{
    private static readonly TimeZoneInfo UtcTimeZone = TimeZoneInfo.Utc;
    private static readonly DateOnly Today = new(2026, 10, 7);
    private static readonly Guid TestPersonId = Guid.NewGuid();
    private const string TestPersonName = "Ada Lovelace";

    [Fact]
    public void Thirty_day_cadence_with_31_days_overdue_returns_reach_out_dto()
    {
        var lastContacted = Today.AddDays(-31);
        var createdAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = ReachOutCalculator.Calculate(
            TestPersonId,
            TestPersonName,
            lastContacted,
            createdAtUtc,
            cadenceDays: 30,
            today: Today,
            userTimeZone: UtcTimeZone);

        result.Should().NotBeNull();
        result!.PersonId.Should().Be(TestPersonId);
        result.PersonDisplayName.Should().Be(TestPersonName);
        result.CadenceDays.Should().Be(30);
        result.ReferenceDate.Should().Be(lastContacted);
        result.DaysSinceContact.Should().Be(31);
        result.DaysOverdue.Should().Be(1);
    }

    [Fact]
    public void Thirty_day_cadence_with_30_days_since_contact_is_not_overdue()
    {
        var lastContacted = Today.AddDays(-30);
        var createdAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = ReachOutCalculator.Calculate(
            TestPersonId,
            TestPersonName,
            lastContacted,
            createdAtUtc,
            cadenceDays: 30,
            today: Today,
            userTimeZone: UtcTimeZone);

        result.Should().BeNull();
    }

    [Fact]
    public void Thirty_day_cadence_with_recent_contact_is_not_overdue()
    {
        var lastContacted = Today.AddDays(-5);
        var createdAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = ReachOutCalculator.Calculate(
            TestPersonId,
            TestPersonName,
            lastContacted,
            createdAtUtc,
            cadenceDays: 30,
            today: Today,
            userTimeZone: UtcTimeZone);

        result.Should().BeNull();
    }

    [Fact]
    public void Null_cadence_returns_null()
    {
        var lastContacted = Today.AddDays(-100);
        var createdAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = ReachOutCalculator.Calculate(
            TestPersonId,
            TestPersonName,
            lastContacted,
            createdAtUtc,
            cadenceDays: null,
            today: Today,
            userTimeZone: UtcTimeZone);

        result.Should().BeNull();
    }

    [Fact]
    public void Zero_or_negative_cadence_returns_null()
    {
        var lastContacted = Today.AddDays(-10);
        var createdAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var zeroResult = ReachOutCalculator.Calculate(
            lastContacted,
            createdAtUtc,
            cadenceDays: 0,
            today: Today,
            userTimeZone: UtcTimeZone);

        var negResult = ReachOutCalculator.Calculate(
            lastContacted,
            createdAtUtc,
            cadenceDays: -5,
            today: Today,
            userTimeZone: UtcTimeZone);

        zeroResult.Should().BeNull();
        negResult.Should().BeNull();
    }

    [Fact]
    public void Null_last_contacted_falls_back_to_created_at_utc_converted_to_user_calendar()
    {
        // CreatedAtUtc is 35 days before Today in UTC
        var createdAtUtc = Today.AddDays(-35).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);

        var result = ReachOutCalculator.Calculate(
            TestPersonId,
            TestPersonName,
            lastContactedOn: null,
            createdAtUtc: createdAtUtc,
            cadenceDays: 30,
            today: Today,
            userTimeZone: UtcTimeZone);

        result.Should().NotBeNull();
        result!.ReferenceDate.Should().Be(Today.AddDays(-35));
        result.DaysSinceContact.Should().Be(35);
        result.DaysOverdue.Should().Be(5);
    }

    [Fact]
    public void Person_overload_returns_null_when_person_is_archived()
    {
        var person = new Person
        {
            Id = TestPersonId,
            FirstName = "Ada",
            IsArchived = true,
            StayInTouchCadenceDays = 30,
            LastContactedOn = Today.AddDays(-50),
        };

        var result = ReachOutCalculator.Calculate(person, Today, UtcTimeZone);

        result.Should().BeNull();
    }

    [Fact]
    public void Person_overload_returns_reach_out_dto_when_active_and_overdue()
    {
        var person = new Person
        {
            Id = TestPersonId,
            FirstName = "Ada",
            LastName = "Lovelace",
            IsArchived = false,
            StayInTouchCadenceDays = 14,
            LastContactedOn = Today.AddDays(-20),
        };

        var result = ReachOutCalculator.Calculate(person, Today, UtcTimeZone);

        result.Should().NotBeNull();
        result!.PersonId.Should().Be(TestPersonId);
        result.PersonDisplayName.Should().Be("Ada Lovelace");
        result.CadenceDays.Should().Be(14);
        result.DaysSinceContact.Should().Be(20);
        result.DaysOverdue.Should().Be(6);
    }
}
