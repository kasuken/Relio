using Microsoft.EntityFrameworkCore;
using Relio.Application.Onboarding;
using Relio.Application.Security;

namespace Relio.Data.Onboarding;

/// <summary>EF Core implementation of the current user's onboarding state (issue #48).</summary>
public sealed class OnboardingService(RelioDbContext dbContext, ICurrentUser currentUser) : IOnboardingService
{
    /// <inheritdoc />
    public async Task<OnboardingState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();
        var isPending = await dbContext.UserProfiles
            .AsNoTracking()
            .Where(profile => profile.OwnerId == ownerId)
            .Select(profile => !profile.OnboardingDismissed)
            .FirstOrDefaultAsync(cancellationToken);

        return new OnboardingState(isPending);
    }

    /// <inheritdoc />
    public async Task DismissAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = currentUser.RequireUserId();

        try
        {
            var profile = await dbContext.UserProfiles
                .FirstOrDefaultAsync(profile => profile.OwnerId == ownerId, cancellationToken);

            if (profile is null || profile.OnboardingDismissed)
            {
                return;
            }

            profile.OnboardingDismissed = true;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }
}
