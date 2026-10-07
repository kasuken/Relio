using Relio.Application.Reminders;
using Relio.Domain;

namespace Relio.Application.Tests.Reminders;

public class BirthdayReminderCalculatorTests
{
    private static readonly Guid TestPersonId = Guid.NewGuid();
    private const string TestPersonName = "Ada Lovelace";

    [Fact]
    public void Lead_days_0_makes_reminder_date_match_birthday_date()
    {
        var birthday = Birthday.Create(10, 15, 1990);
        var today = new DateOnly(2026, 10, 15);

        var dto = BirthdayReminderCalculator.Calculate(
            TestPersonId, TestPersonName, birthday, reminderDisabled: false, personLeadDays: 0, defaultLeadDays: 7, today: today);

        dto.Should().NotBeNull();
        dto!.BirthdayDate.Should().Be(new DateOnly(2026, 10, 15));
        dto.ReminderDate.Should().Be(new DateOnly(2026, 10, 15));
        dto.LeadDays.Should().Be(0);
        dto.IsDue.Should().BeTrue();
        dto.DaysUntilBirthday.Should().Be(0);
    }

    [Fact]
    public void Lead_days_calculates_reminder_date_and_due_status_ahead_of_birthday()
    {
        var birthday = Birthday.Create(10, 15, 1990);
        var today = new DateOnly(2026, 10, 8); // exactly 7 days before

        var dto = BirthdayReminderCalculator.Calculate(
            TestPersonId, TestPersonName, birthday, reminderDisabled: false, personLeadDays: null, defaultLeadDays: 7, today: today);

        dto.Should().NotBeNull();
        dto!.BirthdayDate.Should().Be(new DateOnly(2026, 10, 15));
        dto.ReminderDate.Should().Be(new DateOnly(2026, 10, 8));
        dto.LeadDays.Should().Be(7);
        dto.IsDue.Should().BeTrue();
        dto.DaysUntilBirthday.Should().Be(7);
    }

    [Fact]
    public void Reminder_is_not_due_before_reminder_date()
    {
        var birthday = Birthday.Create(10, 15, 1990);
        var today = new DateOnly(2026, 10, 5); // 10 days before, lead days is 7

        var dto = BirthdayReminderCalculator.Calculate(
            TestPersonId, TestPersonName, birthday, reminderDisabled: false, personLeadDays: 7, defaultLeadDays: 0, today: today);

        dto.Should().NotBeNull();
        dto!.IsDue.Should().BeFalse();
        dto.DaysUntilBirthday.Should().Be(10);
    }

    [Fact]
    public void Person_lead_days_override_takes_precedence_over_default_lead_days()
    {
        var birthday = Birthday.Create(10, 15, 1990);
        var today = new DateOnly(2026, 10, 10);

        var dto = BirthdayReminderCalculator.Calculate(
            TestPersonId, TestPersonName, birthday, reminderDisabled: false, personLeadDays: 14, defaultLeadDays: 3, today: today);

        dto.Should().NotBeNull();
        dto!.LeadDays.Should().Be(14);
        dto.ReminderDate.Should().Be(new DateOnly(2026, 10, 1));
        dto.IsDue.Should().BeTrue();
    }

    [Fact]
    public void February_29_in_non_leap_year_is_observed_on_February_28()
    {
        var birthday = Birthday.Create(2, 29, 2000);
        var today = new DateOnly(2026, 2, 20); // 2026 is non-leap year

        var dto = BirthdayReminderCalculator.Calculate(
            TestPersonId, TestPersonName, birthday, reminderDisabled: false, personLeadDays: null, defaultLeadDays: 0, today: today);

        dto.Should().NotBeNull();
        dto!.BirthdayDate.Should().Be(new DateOnly(2026, 2, 28));
        dto.DaysUntilBirthday.Should().Be(8);
        dto.TurningAge.Should().Be(26);
    }

    [Fact]
    public void February_29_in_leap_year_is_observed_on_February_29()
    {
        var birthday = Birthday.Create(2, 29, 2000);
        var today = new DateOnly(2028, 2, 20); // 2028 is leap year

        var dto = BirthdayReminderCalculator.Calculate(
            TestPersonId, TestPersonName, birthday, reminderDisabled: false, personLeadDays: null, defaultLeadDays: 0, today: today);

        dto.Should().NotBeNull();
        dto!.BirthdayDate.Should().Be(new DateOnly(2028, 2, 29));
        dto.DaysUntilBirthday.Should().Be(9);
        dto.TurningAge.Should().Be(28);
    }

    [Fact]
    public void Turning_age_is_computed_when_year_is_known()
    {
        var birthday = Birthday.Create(5, 20, 1980);
        var today = new DateOnly(2026, 5, 20);

        var dto = BirthdayReminderCalculator.Calculate(
            TestPersonId, TestPersonName, birthday, reminderDisabled: false, personLeadDays: 0, defaultLeadDays: 0, today: today);

        dto.Should().NotBeNull();
        dto!.TurningAge.Should().Be(46);
    }

    [Fact]
    public void Turning_age_is_null_when_year_is_omitted()
    {
        var birthday = Birthday.Create(5, 20); // yearless
        var today = new DateOnly(2026, 5, 20);

        var dto = BirthdayReminderCalculator.Calculate(
            TestPersonId, TestPersonName, birthday, reminderDisabled: false, personLeadDays: 0, defaultLeadDays: 0, today: today);

        dto.Should().NotBeNull();
        dto!.TurningAge.Should().BeNull();
    }

    [Fact]
    public void Returns_null_when_globally_disabled()
    {
        var birthday = Birthday.Create(5, 20, 1980);
        var today = new DateOnly(2026, 5, 20);

        var dto = BirthdayReminderCalculator.Calculate(
            TestPersonId, TestPersonName, birthday, reminderDisabled: false, personLeadDays: 0, defaultLeadDays: 0, today: today, globallyEnabled: false);

        dto.Should().BeNull();
    }

    [Fact]
    public void Returns_null_when_person_reminder_is_disabled()
    {
        var birthday = Birthday.Create(5, 20, 1980);
        var today = new DateOnly(2026, 5, 20);

        var dto = BirthdayReminderCalculator.Calculate(
            TestPersonId, TestPersonName, birthday, reminderDisabled: true, personLeadDays: 0, defaultLeadDays: 0, today: today);

        dto.Should().BeNull();
    }

    [Fact]
    public void Returns_null_when_person_is_archived()
    {
        var person = new Person
        {
            Id = TestPersonId,
            FirstName = "Ada",
            BirthdayDay = 20,
            BirthdayMonth = 5,
            BirthdayYear = 1980,
            IsArchived = true,
        };
        var today = new DateOnly(2026, 5, 20);

        var dto = BirthdayReminderCalculator.Calculate(person, defaultLeadDays: 0, today: today);

        dto.Should().BeNull();
    }

    [Fact]
    public void Returns_null_when_person_has_no_birthday()
    {
        var person = new Person
        {
            Id = TestPersonId,
            FirstName = "Ada",
        };
        var today = new DateOnly(2026, 5, 20);

        var dto = BirthdayReminderCalculator.Calculate(person, defaultLeadDays: 0, today: today);

        dto.Should().BeNull();
    }
}
