using Microsoft.EntityFrameworkCore;
using Relio.Application.Reminders;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Domain;

namespace Relio.Data.Reminders;

/// <summary>
/// EF Core implementation of <see cref="INotificationPreferencesService"/> scoped to the current user (Issue #40).
/// </summary>
public sealed class NotificationPreferencesService(RelioDbContext dbContext, ICurrentUser currentUser)
    : INotificationPreferencesService
{
    /// <inheritdoc />
    public async Task<NotificationPreferencesDto> GetPreferencesAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        var existing = await dbContext.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OwnerId == ownerId, cancellationToken);

        if (existing is not null && !string.IsNullOrWhiteSpace(existing.UnsubscribeToken))
        {
            return new NotificationPreferencesDto(
                existing.ReminderEmailDelivery,
                existing.BirthdayRemindersEnabled,
                existing.DefaultBirthdayLeadDays,
                existing.UnsubscribeToken);
        }

        try
        {
            var profile = await dbContext.UserProfiles
                .FirstOrDefaultAsync(p => p.OwnerId == ownerId, cancellationToken);

            if (profile is null)
            {
                profile = new UserProfile
                {
                    OwnerId = ownerId,
                    TimeZoneId = TimeZoneIds.Default,
                    ReminderEmailDelivery = ReminderEmailDelivery.DailyDigest,
                    BirthdayRemindersEnabled = true,
                    DefaultBirthdayLeadDays = 0,
                    UnsubscribeToken = UnsubscribeTokens.Generate(),
                };
                dbContext.UserProfiles.Add(profile);
            }
            else if (string.IsNullOrWhiteSpace(profile.UnsubscribeToken))
            {
                profile.UnsubscribeToken = UnsubscribeTokens.Generate();
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return new NotificationPreferencesDto(
                profile.ReminderEmailDelivery,
                profile.BirthdayRemindersEnabled,
                profile.DefaultBirthdayLeadDays,
                profile.UnsubscribeToken);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task SetPreferencesAsync(UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = currentUser.RequireUserId();

        if (!Enum.IsDefined(request.Delivery))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Invalid reminder email delivery option.");
        }

        if (request.DefaultBirthdayLeadDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Default birthday lead days cannot be negative.");
        }

        try
        {
            var profile = await dbContext.UserProfiles
                .FirstOrDefaultAsync(p => p.OwnerId == ownerId, cancellationToken);

            if (profile is null)
            {
                profile = new UserProfile
                {
                    OwnerId = ownerId,
                    TimeZoneId = TimeZoneIds.Default,
                    ReminderEmailDelivery = request.Delivery,
                    BirthdayRemindersEnabled = request.BirthdayRemindersEnabled,
                    DefaultBirthdayLeadDays = request.DefaultBirthdayLeadDays,
                    UnsubscribeToken = UnsubscribeTokens.Generate(),
                };
                dbContext.UserProfiles.Add(profile);
            }
            else
            {
                profile.ReminderEmailDelivery = request.Delivery;
                profile.BirthdayRemindersEnabled = request.BirthdayRemindersEnabled;
                profile.DefaultBirthdayLeadDays = request.DefaultBirthdayLeadDays;
                if (string.IsNullOrWhiteSpace(profile.UnsubscribeToken))
                {
                    profile.UnsubscribeToken = UnsubscribeTokens.Generate();
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }
}
