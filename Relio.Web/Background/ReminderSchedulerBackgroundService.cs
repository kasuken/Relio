using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relio.Application.Reminders;

namespace Relio.Web.Background;

/// <summary>
/// Hosted background service that periodically triggers <see cref="IReminderSchedulerRunner"/>
/// to check and deliver due reminders (epic #36, issue #39).
/// </summary>
public sealed class ReminderSchedulerBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<ReminderSchedulerOptions> options,
    ILogger<ReminderSchedulerBackgroundService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        if (!config.Enabled)
        {
            logger.LogInformation("Reminder scheduler background service is disabled.");
            return;
        }

        var interval = config.CheckInterval > TimeSpan.Zero ? config.CheckInterval : TimeSpan.FromHours(1);
        logger.LogInformation("Reminder scheduler background service started with check interval {Interval}.", interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<IReminderSchedulerRunner>();
                var delivered = await runner.RunDueRemindersJobAsync(stoppingToken);
                logger.LogInformation("Reminder scheduler run completed. Delivered {DeliveredCount} reminders.", delivered);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    "Reminder scheduler job failed with exception type {ExceptionType}.",
                    ex.GetType().Name);
            }

            try
            {
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Reminder scheduler background service stopped.");
    }
}
