using DotBoxD.Plugins;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Transport;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginConnectionHostLifecycleTests
{
    private static readonly TimeSpan Timeout5s = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StopAsync_completes_lifecycle_tasks_when_no_peer_connected()
    {
        // A local stop tears the listener down without raising the peer's Disconnected event. The host must still
        // complete its lifecycle tasks so a caller awaiting them never hangs: Connected is canceled (no peer ever
        // arrived) and Disconnected completes.
        using var server = PluginServer.Create();
        await using var host = await PluginConnectionHost<object>.StartAsync(server, FreshPipeName(), Configure);

        await host.StopAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await host.Connected.WaitAsync(Timeout5s));
        await host.Disconnected.WaitAsync(Timeout5s);
    }

    [Fact]
    public async Task StopAsync_completes_lifecycle_tasks_when_transport_stop_throws()
    {
        using var server = PluginServer.Create();
        var transport = new StopThrowingServerTransport();
        var host = await PluginConnectionHost<object>.StartAsync(server, transport, Configure);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(host.StopAsync);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await host.Connected.WaitAsync(Timeout5s));
            await host.Disconnected.WaitAsync(Timeout5s);
        }
        finally
        {
            await DisposeAfterFailedStopAsync(host);
        }
    }

    [Fact]
    public async Task DisposeAsync_completes_lifecycle_tasks_when_no_peer_connected()
    {
        using var server = PluginServer.Create();
        var host = await PluginConnectionHost<object>.StartAsync(server, FreshPipeName(), Configure);

        await host.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await host.Connected.WaitAsync(Timeout5s));
        await host.Disconnected.WaitAsync(Timeout5s);
    }

    [Fact]
    public async Task StartAsync_disposes_owned_transport_when_transport_start_fails()
    {
        using var server = PluginServer.Create();
        var transport = new ThrowingStartTransport();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => PluginConnectionHost<object>.StartAsync(server, transport, Configure));

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task StartAsync_preserves_transport_start_failure_when_cleanup_dispose_throws()
    {
        using var server = PluginServer.Create();
        var startFailure = new InvalidOperationException("transport start failed");
        var disposeFailure = new ApplicationException("dispose failed");
        var transport = new ThrowingStartTransport(startFailure, disposeFailure);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PluginConnectionHost<object>.StartAsync(server, transport, Configure));

        Assert.Same(startFailure, ex);
        Assert.Same(disposeFailure, ex.Data["PluginConnectionHost.StartCleanupException"]);
        Assert.Equal(1, transport.DisposeCount);
    }

    private static object Configure(DotBoxD.Services.Peer.RpcPeer peer, PluginSession session) => new();

    // A high-entropy pipe name (>= 32 chars with an unguessable random component) so it passes the safe-name
    // validation without opting into unsafe development names.
    private static string FreshPipeName() => "dotboxd-lifecycle-test-" + Guid.NewGuid().ToString("N");

    private static async ValueTask DisposeAfterFailedStopAsync(PluginConnectionHost<object> host)
    {
        try
        {
            await host.DisposeAsync();
        }
        catch (InvalidOperationException ex) when (ex.Message == "stop boom")
        {
            // The transport intentionally throws on stop; disposal still runs RpcHost's cleanup finally.
        }
    }

    private sealed class ThrowingStartTransport : IServerTransport
    {
        private readonly Exception _startFailure;
        private readonly Exception? _disposeFailure;
        private int _disposeCount;

        public ThrowingStartTransport()
            : this(new InvalidOperationException("transport start failed"))
        {
        }

        public ThrowingStartTransport(Exception startFailure, Exception? disposeFailure = null)
        {
            _startFailure = startFailure;
            _disposeFailure = disposeFailure;
        }

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Task StartAsync(CancellationToken ct = default) => throw _startFailure;

        public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Accept should not run when start fails.");

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            if (_disposeFailure is not null)
            {
                throw _disposeFailure;
            }

            return default;
        }
    }

    private sealed class StopThrowingServerTransport : IServerTransport
    {
        private readonly TaskCompletionSource<bool> _parked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            using (ct.Register(static s => ((TaskCompletionSource<bool>)s!).TrySetResult(true), _parked))
            {
                await _parked.Task.ConfigureAwait(false);
            }

            throw new OperationCanceledException(ct);
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            _parked.TrySetResult(true);
            throw new InvalidOperationException("stop boom");
        }

        public ValueTask DisposeAsync()
        {
            _parked.TrySetResult(true);
            return default;
        }
    }
}
