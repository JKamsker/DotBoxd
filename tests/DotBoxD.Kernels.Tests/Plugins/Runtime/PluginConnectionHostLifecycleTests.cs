using DotBoxD.Plugins;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginConnectionHostLifecycleTests
{
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
            async () => await host.Connected.WaitAsync(TimeSpan.FromSeconds(5)));
        await host.Disconnected.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DisposeAsync_completes_lifecycle_tasks_when_no_peer_connected()
    {
        using var server = PluginServer.Create();
        var host = await PluginConnectionHost<object>.StartAsync(server, FreshPipeName(), Configure);

        await host.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await host.Connected.WaitAsync(TimeSpan.FromSeconds(5)));
        await host.Disconnected.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static object Configure(DotBoxD.Services.Peer.RpcPeer peer, PluginSession session) => new();

    // A high-entropy pipe name (>= 32 chars with an unguessable random component) so it passes the safe-name
    // validation without opting into unsafe development names.
    private static string FreshPipeName() => "dotboxd-lifecycle-test-" + Guid.NewGuid().ToString("N");
}
