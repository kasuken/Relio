using Relio.Data.Concurrency;

namespace Relio.Data.Tests.Concurrency;

/// <summary>
/// Proves <see cref="DatabaseLane"/> without a database: one operation at a time, re-entrant within
/// one async flow, and - the invariant everything rests on - sibling calls started back to back by
/// the same caller are NOT mistaken for re-entrancy.
/// </summary>
public class DatabaseLaneTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Operations_started_together_run_one_at_a_time()
    {
        var lane = new DatabaseLane();
        var release = new TaskCompletionSource();
        var firstStarted = new TaskCompletionSource();
        var secondRan = false;

        var first = lane.RunAsync(async () =>
        {
            firstStarted.SetResult();
            await release.Task;
        });
        var second = lane.RunAsync(() =>
        {
            secondRan = true;
            return Task.CompletedTask;
        });

        await firstStarted.Task.WaitAsync(Timeout);
        await Task.Delay(50);
        secondRan.Should().BeFalse("the second operation waits for the first");

        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(Timeout);

        secondRan.Should().BeTrue();
    }

    [Fact]
    public async Task Calls_started_back_to_back_from_one_method_are_not_treated_as_reentrant()
    {
        var lane = new DatabaseLane();
        var release = new TaskCompletionSource();
        var firstStarted = new TaskCompletionSource();
        var secondStarted = false;

        // The shape of two sibling components' OnInitializedAsync: one caller, no await in between.
        var first = lane.RunAsync(async () =>
        {
            firstStarted.SetResult();
            await release.Task;
        });
        lane.IsHeldByCurrentFlow.Should().BeFalse("the caller of RunAsync never inherits the held marker");
        var second = lane.RunAsync(() =>
        {
            secondStarted = true;
            return Task.CompletedTask;
        });

        await firstStarted.Task.WaitAsync(Timeout);
        await Task.Delay(50);
        secondStarted.Should().BeFalse();

        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(Timeout);
        secondStarted.Should().BeTrue();
    }

    [Fact]
    public async Task A_nested_call_in_the_same_flow_does_not_deadlock()
    {
        var lane = new DatabaseLane();

        var result = await lane.RunAsync(async () =>
        {
            lane.IsHeldByCurrentFlow.Should().BeTrue();
            return await lane.RunAsync(() => Task.FromResult(42));
        }).WaitAsync(Timeout);

        result.Should().Be(42);
        lane.IsHeldByCurrentFlow.Should().BeFalse("the marker is gone once the operation ends");
    }

    [Fact]
    public async Task A_failing_operation_releases_the_lane()
    {
        var lane = new DatabaseLane();

        var act = () => lane.RunAsync(() => throw new InvalidOperationException("boom"));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        var ran = false;
        await lane.RunAsync(() =>
        {
            ran = true;
            return Task.CompletedTask;
        }).WaitAsync(Timeout);
        ran.Should().BeTrue();
    }

    [Fact]
    public async Task A_cancelled_wait_does_not_take_the_lane()
    {
        var lane = new DatabaseLane();
        var release = new TaskCompletionSource();
        var firstStarted = new TaskCompletionSource();
        var first = lane.RunAsync(async () =>
        {
            firstStarted.SetResult();
            await release.Task;
        });
        await firstStarted.Task.WaitAsync(Timeout);

        using var cancellation = new CancellationTokenSource();
        var secondRan = false;
        var second = lane.RunAsync(
            () =>
            {
                secondRan = true;
                return Task.CompletedTask;
            },
            cancellation.Token);
        await cancellation.CancelAsync();

        var act = () => second;
        await act.Should().ThrowAsync<OperationCanceledException>();
        secondRan.Should().BeFalse();

        release.SetResult();
        await first.WaitAsync(Timeout);

        var thirdRan = false;
        await lane.RunAsync(() =>
        {
            thirdRan = true;
            return Task.CompletedTask;
        }).WaitAsync(Timeout);
        thirdRan.Should().BeTrue("a cancelled waiter must not leave the lane taken");
    }

    [Fact]
    public async Task Two_lanes_do_not_block_each_other()
    {
        var laneA = new DatabaseLane();
        var laneB = new DatabaseLane();
        var release = new TaskCompletionSource();
        var aStarted = new TaskCompletionSource();
        var a = laneA.RunAsync(async () =>
        {
            aStarted.SetResult();
            await release.Task;
        });
        await aStarted.Task.WaitAsync(Timeout);

        var bRan = false;
        await laneB.RunAsync(() =>
        {
            bRan = true;
            return Task.CompletedTask;
        }).WaitAsync(Timeout);

        bRan.Should().BeTrue();
        release.SetResult();
        await a.WaitAsync(Timeout);
    }

    [Fact]
    public async Task A_flow_holding_one_lane_still_queues_on_another()
    {
        var outer = new DatabaseLane();
        var inner = new DatabaseLane();
        var release = new TaskCompletionSource();
        var holderStarted = new TaskCompletionSource();
        var holder = inner.RunAsync(async () =>
        {
            holderStarted.SetResult();
            await release.Task;
        });
        await holderStarted.Task.WaitAsync(Timeout);

        var ran = false;
        var nested = outer.RunAsync(async () =>
        {
            // Holding `outer` says nothing about `inner`, which another flow holds.
            inner.IsHeldByCurrentFlow.Should().BeFalse();
            await inner.RunAsync(() =>
            {
                ran = true;
                return Task.CompletedTask;
            });
        });

        await Task.Delay(50);
        ran.Should().BeFalse();

        release.SetResult();
        await Task.WhenAll(holder, nested).WaitAsync(Timeout);
        ran.Should().BeTrue();
    }
}
