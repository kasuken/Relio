using Microsoft.EntityFrameworkCore;
using Relio.Application.Profile;
using Relio.Application.Security;
using Relio.Application.Time;
using Relio.Domain;

namespace Relio.Data.Profile;

/// <summary>
/// EF Core-backed implementation of <see cref="IUserProfileService"/>. Lives in Relio.Data (not
/// Relio.Application) because it depends on <see cref="RelioDbContext"/> directly, like
/// <c>Relio.Data.Time.UserTimeZoneService</c> - see the "User-scoped data pattern" section of
/// AGENTS.md. Every query and mutation is explicitly filtered by <see cref="IOwnedEntity.OwnerId"/>.
/// </summary>
public sealed class UserProfileService(RelioDbContext dbContext, ICurrentUser currentUser) : IUserProfileService
{
    /// <inheritdoc />
    public async Task<string?> GetDisplayNameAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        return await dbContext.UserProfiles
            .AsNoTracking()
            .Where(p => p.OwnerId == ownerId)
            .Select(p => p.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetDisplayNameAsync(string? displayName, CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        // Validate before touching the database: a bad value must leave the profile untouched.
        var normalized = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        if (normalized is { Length: > UserProfile.DisplayNameMaxLength })
        {
            throw new ArgumentException(
                $"A display name can be at most {UserProfile.DisplayNameMaxLength} characters long.",
                nameof(displayName));
        }

        var profile = await dbContext.UserProfiles
            .FirstOrDefaultAsync(p => p.OwnerId == ownerId, cancellationToken);

        if (profile is null)
        {
            dbContext.UserProfiles.Add(new UserProfile
            {
                OwnerId = ownerId,
                TimeZoneId = TimeZoneIds.Default,
                DisplayName = normalized,
            });
        }
        else
        {
            profile.DisplayName = normalized;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
