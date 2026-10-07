using Relio.Web.Security;

namespace Relio.Web.Tests.Security;

public sealed class AccountSessionRevocationNotifierTests
{
    [Fact]
    public async Task Revocation_notifies_only_current_subscribers_for_the_matching_account()
    {
        var notifier = new AccountSessionRevocationNotifier();
        var firstCalls = 0;
        var secondCalls = 0;
        using var first = notifier.Subscribe(
            "account-a",
            _ =>
            {
                firstCalls++;
                return Task.CompletedTask;
            });
        using var second = notifier.Subscribe(
            "account-b",
            _ =>
            {
                secondCalls++;
                return Task.CompletedTask;
            });

        await notifier.RevokeActiveAsync("account-a", AccountSessionRevocationReason.Deleted);

        firstCalls.Should().Be(1);
        secondCalls.Should().Be(0);
    }

    [Fact]
    public async Task Disposed_subscriptions_are_removed_and_revocation_is_not_retained()
    {
        var notifier = new AccountSessionRevocationNotifier();
        var calls = 0;
        var subscription = notifier.Subscribe(
            "account-a",
            _ =>
            {
                calls++;
                return Task.CompletedTask;
            });

        subscription.Dispose();
        await notifier.RevokeActiveAsync("account-a", AccountSessionRevocationReason.Disabled);

        calls.Should().Be(0);
    }

    [Fact]
    public async Task Revoke_active_awaits_subscriber_callbacks()
    {
        var notifier = new AccountSessionRevocationNotifier();
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = notifier.Subscribe(
            "account-a",
            async _ =>
            {
                callbackStarted.TrySetResult();
                await releaseCallback.Task;
            });

        var revoke = notifier.RevokeActiveAsync("account-a", AccountSessionRevocationReason.Deleted);
        await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        revoke.IsCompleted.Should().BeFalse();

        releaseCallback.TrySetResult();
        await revoke;
    }
}
