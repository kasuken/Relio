using Relio.Application.Reminders;
using Relio.Domain;

namespace Relio.Application.Tests.Reminders;

public class ReminderRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_title_produces_TitleRequired(string? title)
    {
        var request = new CreateReminderRequest
        {
            PersonId = Guid.NewGuid(),
            Title = title!,
            DueDate = new DateOnly(2026, 10, 10),
        };

        var errors = ReminderRules.Validate(request);

        errors.Should().Contain(ReminderValidationError.TitleRequired);
    }

    [Fact]
    public void Title_longer_than_max_produces_TitleTooLong()
    {
        var request = new CreateReminderRequest
        {
            PersonId = Guid.NewGuid(),
            Title = new string('A', Reminder.TitleMaxLength + 1),
            DueDate = new DateOnly(2026, 10, 10),
        };

        var errors = ReminderRules.Validate(request);

        errors.Should().Contain(ReminderValidationError.TitleTooLong);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(null)]
    public void CustomMonths_frequency_without_positive_interval_produces_InvalidCustomInterval(int? interval)
    {
        var request = new CreateReminderRequest
        {
            PersonId = Guid.NewGuid(),
            Title = "Valid title",
            DueDate = new DateOnly(2026, 10, 10),
            Frequency = ReminderFrequency.CustomMonths,
            CustomIntervalMonths = interval,
        };

        var errors = ReminderRules.Validate(request);

        errors.Should().Contain(ReminderValidationError.InvalidCustomInterval);
    }

    [Fact]
    public void Valid_request_produces_no_errors()
    {
        var request = new CreateReminderRequest
        {
            PersonId = Guid.NewGuid(),
            Title = "Catch up over coffee",
            DueDate = new DateOnly(2026, 10, 10),
            Frequency = ReminderFrequency.Monthly,
        };

        var errors = ReminderRules.Validate(request);

        errors.Should().BeEmpty();
    }
}
