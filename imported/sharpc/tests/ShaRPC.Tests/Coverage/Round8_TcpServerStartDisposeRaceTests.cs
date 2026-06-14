using System.Net;
using ShaRPC.Transports.Tcp;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round 8 regression for <see cref="TcpServerTransport.StartAsync"/>. There is a TOCTOU window between the
/// <c>_disposed</c> guard and the <c>_listener = listener</c> publish: if <c>DisposeAsync</c> runs entirely
/// inside it, its <c>Interlocked.Exchange(_listener, null)</c> is a no-op (the field is still null), then
/// StartAsync publishes a live, bound <see cref="System.Net.Sockets.TcpListener"/> into a transport whose
/// <c>_disposed == 1</c>. No later path ever stops it (the dispose idempotency guard blocks retry), so the
/// OS port leaks until process exit. The client-side sibling <c>TcpTransport.ConnectAsync</c> already
/// handles the equivalent race with a Dekker-style barrier + post-publish re-check; the server must too.
/// </summary>
public sealed class Round8_TcpServerStartDisposeRaceTests
{
    [Fact]
    public async Task StartAsync_WhenDisposedDuringStart_StopsTheOrphanedListener()
    {
        var server = new TcpServerTransport(IPAddress.Loopback, 0);

        // Drive the race deterministically: DisposeAsync runs entirely after the _disposed check passed
        // but before the listener is published.
        server._onListenerStartedBeforePublishForTest = () =>
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();

        // The fix detects _disposed after publishing, stops the orphaned listener, and throws. On the bug,
        // StartAsync publishes the live listener into a disposed transport and returns without throwing.
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await server.StartAsync());

        // The listener must not be left bound/orphaned.
        Assert.Null(server.LocalEndpoint);
    }
}
