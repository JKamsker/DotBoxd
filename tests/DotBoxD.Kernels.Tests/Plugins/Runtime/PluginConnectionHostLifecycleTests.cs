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

        await Assert.ThrowsAsync<InvalidOperationException>(host.StopAsync);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await host.Connected.WaitAsync(Timeout5s));
        await host.Disconnected.WaitAsync(Timeout5s);
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

    private static object Configure(DotBoxD.Services.Peer.RpcPeer peer, PluginSession session) => new();

    // A high-entropy pipe name (>= 32 chars with an unguessable random component) so it passes the safe-name
    // validation without opting into unsafe development names.
    private static string FreshPipeName() => "dotboxd-lifecycle-test-" + Guid.NewGuid().ToString("N");

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
