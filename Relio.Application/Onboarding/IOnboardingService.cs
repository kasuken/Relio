namespace Relio.Application.Onboarding;

/// <summary>
/// Reads and dismisses the first-run guide for the authenticated user. The service never enrolls
/// an account whose profile does not already exist.
/// </summary>
public interface IOnboardingService
{
    /// <summary>
    /// Gets whether the guide is pending for the current user. A missing profile is treated as
    /// dismissed so legacy and programmatically-created accounts are not enrolled.
    /// </summary>
    Task<OnboardingState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks the guide as completed or skipped for the current user.</summary>
    Task DismissAsync(CancellationToken cancellationToken = default);
}
