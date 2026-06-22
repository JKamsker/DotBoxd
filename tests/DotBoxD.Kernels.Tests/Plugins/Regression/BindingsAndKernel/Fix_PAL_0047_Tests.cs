using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Regression.BindingsAndKernel;

/// <summary>
/// Regression coverage for issue #30: horizontal kernel pools allow async plugin work to
/// overlap without relaxing the single-threaded execution gate inside each child kernel.
/// </summary>
public sealed class Fix_PAL_0047_Tests
{
    [Fact]
    public async Task Kernel_pool_dispatches_async_events_across_children()
    {
        var messages = new BlockingCountingSink();
        using var server = PluginAddendumTestPolicies.CreateServer(
            messages,
            executionMode: ExecutionMode.Compiled);
        var pool = await server.InstallPoolAsync(
            FireDamagePluginPackage.Create(),
            degreeOfParallelism: 2);
        server.Hooks.On<DamageEvent>().Use(pool);

        var first = server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1")).AsTask();
        await messages.WaitForStartedCountAsync(1);
        var second = server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-2")).AsTask();

        await messages.WaitForStartedCountAsync(2);
        messages.ReleaseAll();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, pool.DegreeOfParallelism);
        Assert.Equal(2, messages.Messages.Count);
        // Both events carry distinct per-event payloads through the shared ExecutionPlan; assert the
        // outputs are not swapped or duplicated, proving each child kernel marshalled its own event
        // rather than racing on plan-attached state.
        Assert.Equal(
            new[] { "player-1", "player-2" },
            messages.Messages.Select(m => m.TargetId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(pool.Kernels, kernel => Assert.Equal(2, kernel.ExecutionObservations.Count));
    }

    [Fact]
    public async Task Uninstall_pool_detaches_handlers_and_revokes_children()
    {
        var messages = new BlockingCountingSink();
        using var server = PluginAddendumTestPolicies.CreateServer(messages);
        var pool = await server.InstallPoolAsync(
            FireDamagePluginPackage.Create(),
            degreeOfParallelism: 2);
        server.Hooks.On<DamageEvent>().Use(pool);

        Assert.True(server.UninstallPool(pool));
        Assert.True(pool.IsRevoked);

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"))
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(messages.Messages);
        Assert.False(server.UninstallPool(pool));
    }

    [Fact]
    public async Task Uninstall_pool_cancels_in_flight_child_execution()
    {
        var messages = new BlockingCountingSink();
        using var server = PluginAddendumTestPolicies.CreateServer(messages);
        var pool = await server.InstallPoolAsync(
            FireDamagePluginPackage.Create(),
            degreeOfParallelism: 2);
        server.Hooks.On<DamageEvent>().Use(pool);

        var publish = server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1")).AsTask();
        await messages.WaitForStartedCountAsync(1);
        Assert.True(server.UninstallPool(pool));

        var error = await Record.ExceptionAsync(
            async () => await publish.WaitAsync(TimeSpan.FromSeconds(5)));
        messages.ReleaseAll();

        Assert.True(
            error is OperationCanceledException ||
            error is SandboxRuntimeException { Error.Code: SandboxErrorCode.PolicyDenied },
            error?.ToString());
        Assert.Empty(messages.Messages);
    }

    [Fact]
    public async Task Removing_a_kernel_that_is_only_a_pool_member_does_not_detach_the_whole_pool()
    {
        var messages = new BlockingCountingSink();
        messages.ReleaseAll();
        using var server = PluginAddendumTestPolicies.CreateServer(messages);
        var pool = await server.InstallPoolAsync(
            FireDamagePluginPackage.Create(),
            degreeOfParallelism: 2);
        var pipeline = server.Hooks.On<DamageEvent>().Use(pool);

        // A pool member was never registered as an individual kernel handler, so removing it must be
        // a no-op. The ContainingPool fallback wrongly treats it as a request to detach the entire pool.
        ((IKernelHandlerPipeline)pipeline).RemoveKernel(pool.Kernels[0]);

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"))
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(messages.Messages);
    }

    [Fact]
    public async Task WaitForStartedCount_fails_fast_when_target_is_unreachable()
    {
        var sink = new BlockingCountingSink();

        // No SendAsync is ever invoked, so the started count can never reach 1. The handshake helper
        // must surface a bounded TimeoutException rather than looping forever, so a future dispatch
        // regression fails the test fast instead of hanging until the CI job times out.
        await Assert.ThrowsAsync<TimeoutException>(
            () => sink.WaitForStartedCountAsync(1, TimeSpan.FromMilliseconds(200)));
    }

    private sealed class BlockingCountingSink : IPluginMessageSink
    {
        private readonly object _gate = new();
        private readonly List<PluginMessage> _messages = [];
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _started = new(0);
        private int _startedCount;

        public IReadOnlyList<PluginMessage> Messages
        {
            get
            {
                lock (_gate)
                {
                    return [.. _messages];
                }
            }
        }

        public async ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _startedCount);
            _started.Release();
            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _messages.Add(new PluginMessage(targetId, message));
            }
        }

        public async Task WaitForStartedCountAsync(int expected, TimeSpan? timeout = null)
        {
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
            while (Volatile.Read(ref _startedCount) < expected)
            {
                try
                {
                    await _started.WaitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException(
                        $"Only {Volatile.Read(ref _startedCount)} of {expected} plugin sends started before the timeout elapsed.");
                }
            }
        }

        public void ReleaseAll() => _release.TrySetResult();
    }
}
