using Relio.Application.Accounts;
using Relio.Application.Administration;
using Relio.Application.People;
using Relio.Application.Profile;
using Relio.Application.Time;
using Relio.Data.Concurrency;

namespace Relio.Data.Tests.Concurrency;

/// <summary>
/// Proves <see cref="DatabaseLaneProxy{TService}"/>: calls through proxies that share a lane queue,
/// the service's own exception types and results come back unchanged, the method's cancellation
/// token cancels the wait, and a service that calls another proxied service on the same lane does not
/// deadlock.
/// </summary>
public class DatabaseLaneProxyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Public so DispatchProxy can generate a proxy for it.</summary>
    public interface IProbe
    {
        Task DoAsync(CancellationToken cancellationToken = default);

        Task<int> CountAsync(int input, CancellationToken cancellationToken = default);

        Task FailAsync(CancellationToken cancellationToken = default);

        Task FailSynchronouslyAsync(string? name);

        Task<int> CallOtherAsync();
    }

    /// <summary>A second probe-like service the first calls into.</summary>
    public interface IOther
    {
        Task<int> OneAsync();
    }

    public sealed class ProbeException(string message) : Exception(message);

    private sealed class Probe(Func<Task>? onDo = null, IOther? other = null) : IProbe
    {
        public Task DoAsync(CancellationToken cancellationToken = default) => onDo?.Invoke() ?? Task.CompletedTask;

        public Task<int> CountAsync(int input, CancellationToken cancellationToken = default) => Task.FromResult(input + 1);

        public async Task FailAsync(CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw new ProbeException("custom failure");
        }

        // Not async: throws before it ever returns a task.
        public Task FailSynchronouslyAsync(string? name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return Task.CompletedTask;
        }

        public Task<int> CallOtherAsync() => other!.OneAsync();
    }

    private sealed class Other : IOther
    {
        public Task<int> OneAsync() => Task.FromResult(1);
    }

    [Fact]
    public async Task Calls_through_two_proxies_sharing_one_lane_run_one_at_a_time()
    {
        var lane = new DatabaseLane();
        var release = new TaskCompletionSource();
        var firstStarted = new TaskCompletionSource();
        var secondRan = false;
        var first = DatabaseLaneProxy<IProbe>.Create(
            new Probe(async () =>
            {
                firstStarted.SetResult();
                await release.Task;
            }),
            lane);
        var second = DatabaseLaneProxy<IProbe>.Create(
            new Probe(() =>
            {
                secondRan = true;
                return Task.CompletedTask;
            }),
            lane);

        var firstCall = first.DoAsync();
        var secondCall = second.DoAsync();
        await firstStarted.Task.WaitAsync(Timeout);
        await Task.Delay(50);
        secondRan.Should().BeFalse();

        release.SetResult();
        await Task.WhenAll(firstCall, secondCall).WaitAsync(Timeout);
        secondRan.Should().BeTrue();
    }

    [Fact]
    public async Task Results_come_back_unchanged()
    {
        var proxy = DatabaseLaneProxy<IProbe>.Create(new Probe(), new DatabaseLane());

        (await proxy.CountAsync(41)).Should().Be(42);
    }

    [Fact]
    public async Task An_async_exception_surfaces_as_its_own_type()
    {
        var proxy = DatabaseLaneProxy<IProbe>.Create(new Probe(), new DatabaseLane());

        var act = () => proxy.FailAsync();

        await act.Should().ThrowAsync<ProbeException>().WithMessage("custom failure");
    }

    [Fact]
    public async Task A_synchronous_exception_arrives_as_a_faulted_task_of_its_own_type()
    {
        var proxy = DatabaseLaneProxy<IProbe>.Create(new Probe(), new DatabaseLane());

        // No exception at the call: a normal async method never throws before it returns its task.
        Task? task = null;
        Action call = () => { task = proxy.FailSynchronouslyAsync(null); };
        call.Should().NotThrow();

        var act = () => task!;
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task The_methods_cancellation_token_cancels_the_wait_for_the_lane()
    {
        var lane = new DatabaseLane();
        var release = new TaskCompletionSource();
        var firstStarted = new TaskCompletionSource();
        var secondRan = false;
        var first = DatabaseLaneProxy<IProbe>.Create(
            new Probe(async () =>
            {
                firstStarted.SetResult();
                await release.Task;
            }),
            lane);
        var second = DatabaseLaneProxy<IProbe>.Create(
            new Probe(() =>
            {
                secondRan = true;
                return Task.CompletedTask;
            }),
            lane);
        var firstCall = first.DoAsync();
        await firstStarted.Task.WaitAsync(Timeout);

        using var cancellation = new CancellationTokenSource();
        var secondCall = second.DoAsync(cancellation.Token);
        await cancellation.CancelAsync();

        var act = () => secondCall;
        await act.Should().ThrowAsync<OperationCanceledException>();
        secondRan.Should().BeFalse();

        release.SetResult();
        await firstCall.WaitAsync(Timeout);
    }

    [Fact]
    public async Task A_service_that_calls_another_proxied_service_on_the_same_lane_completes()
    {
        var lane = new DatabaseLane();
        var other = DatabaseLaneProxy<IOther>.Create(new Other(), lane);
        var proxy = DatabaseLaneProxy<IProbe>.Create(new Probe(other: other), lane);

        var result = await proxy.CallOtherAsync().WaitAsync(Timeout);

        result.Should().Be(1);
    }

    [Fact]
    public void ThrowIfUnsupported_rejects_an_interface_with_a_synchronous_method_or_a_property()
    {
        var syncAct = DatabaseLaneProxy<ISynchronous>.ThrowIfUnsupported;
        var propertyAct = DatabaseLaneProxy<IWithProperty>.ThrowIfUnsupported;
        var classAct = DatabaseLaneProxy<Other>.ThrowIfUnsupported;

        syncAct.Should().Throw<InvalidOperationException>().WithMessage("*Compute*");
        propertyAct.Should().Throw<InvalidOperationException>().WithMessage("*Name*");
        classAct.Should().Throw<InvalidOperationException>().WithMessage("*must be an interface*");
    }

    [Fact]
    public void ThrowIfUnsupported_accepts_every_real_data_service_interface()
    {
        var acts = new Action[]
        {
            DatabaseLaneProxy<IPeopleService>.ThrowIfUnsupported,
            DatabaseLaneProxy<IRelationshipTypeService>.ThrowIfUnsupported,
            DatabaseLaneProxy<IUserTimeZoneService>.ThrowIfUnsupported,
            DatabaseLaneProxy<IUserProfileService>.ThrowIfUnsupported,
            DatabaseLaneProxy<ITwoFactorStatusService>.ThrowIfUnsupported,
            DatabaseLaneProxy<IAccountRegistrationService>.ThrowIfUnsupported,
            DatabaseLaneProxy<IUserAdministrationService>.ThrowIfUnsupported,
        };

        foreach (var act in acts)
        {
            act.Should().NotThrow();
        }
    }

    public interface ISynchronous
    {
        int Compute();
    }

    public interface IWithProperty
    {
        string Name { get; }
    }
}
