using Relio.Application.Onboarding;

namespace Relio.Web.Tests.Onboarding;

internal sealed class FakeOnboardingService(bool isPending = true) : IOnboardingService
{
    public bool IsPending { get; private set; } = isPending;

    public int StateReads { get; private set; }

    public int Dismissals { get; private set; }

    public Exception? ThrowOnNextGet { get; set; }

    public Exception? ThrowOnNextDismiss { get; set; }

    public Task<OnboardingState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        StateReads++;
        if (ThrowOnNextGet is { } exception)
        {
            ThrowOnNextGet = null;
            return Task.FromException<OnboardingState>(exception);
        }

        return Task.FromResult(new OnboardingState(IsPending));
    }

    public Task DismissAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextDismiss is { } exception)
        {
            ThrowOnNextDismiss = null;
            return Task.FromException(exception);
        }

        Dismissals++;
        IsPending = false;
        return Task.CompletedTask;
    }
}
