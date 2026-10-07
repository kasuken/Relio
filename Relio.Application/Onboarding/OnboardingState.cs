namespace Relio.Application.Onboarding;

/// <summary>The current user's onboarding status, without exposing their profile or personal content.</summary>
/// <param name="IsPending">Whether the user may still complete or skip the first-run guide.</param>
public sealed record OnboardingState(bool IsPending);
