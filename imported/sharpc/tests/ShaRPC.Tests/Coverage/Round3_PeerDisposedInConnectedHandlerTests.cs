using ShaRPC.Core;
using ShaRPC.Core.Transport;
using ShaRPC.Tests;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// DETERMINISTIC red→green regression for DEFECT #2 (round 3): disposing an accepted peer from a
/// <see cref="RpcHost.PeerConnected"/> handler must not raise a spurious <see cref="RpcHost.AcceptError"/>.
///
/// <para>
/// <c>RpcHost.AddPeerAsync</c> adds the peer to its collection and subscribes <c>Disconnected</c>,
/// then raises <c>PeerConnected</c> and immediately calls <c>peer.Start()</c>. <c>PeerConnected</c>
/// is the documented post-accept application hook, and disposing the peer there is a natural
/// access-control pattern. <c>RpcPeer.DisposeAsync</c> sets <c>_disposed = 1</c> synchronously, so a
/// handler that disposes the peer makes the following <c>peer.Start()</c> (which runs
/// <c>EnsureStarted</c>) throw <see cref="ObjectDisposedException"/>.
/// </para>
///
/// <para>
/// On the unfixed code that ODE escapes <c>AddPeerAsync</c> into the accept loop's
/// <c>TrackHandoff</c> catch, which raises <c>AcceptError</c> (violating its transport-failure-only
/// contract) and disposes the channel — while the peer is never removed from the host's collection
/// and <c>Disconnected</c> is never unsubscribed (the read loop never started, so
/// <c>OnPeerDisconnected</c> never fires).
/// </para>
///
/// <para>
/// Desired behaviour (what the fix delivers): the in-flight peer disposal is observed and absorbed,
/// so <c>AcceptError</c> does NOT fire and the host still stops cleanly. The test asserts that
/// correct behaviour, so it is RED today and turns green once <c>AddPeerAsync</c> catches the
/// <see cref="ObjectDisposedException"/> from the raise/<c>Start()</c> pair.
/// </para>
///
/// <para>
/// Determinism without sleeps/stress: the host drains in-flight hand-offs inside <c>StopAsync</c>
/// (<c>DrainInFlightAsync</c> awaits the hand-off task that runs <c>AddPeerAsync</c>). The
/// hand-off's catch raises <c>AcceptError</c> synchronously before that task completes, so once
/// <c>StopAsync</c> returns the buggy <c>AcceptError</c> would already have fired. Asserting it did
/// not fire after <c>StopAsync</c> is therefore deterministic every run.
/// </para>
/// </summary>
public sealed class Round3_PeerDisposedInConnectedHandlerTests
{
    private static readonly TimeSpan Timeout5s = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Timeout10s = TimeSpan.FromSeconds(10);

    private static ShaRPC.Serializers.MessagePack.MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task PeerConnectedHandlerDisposesPeer_DoesNotRaiseAcceptError_AndHostStopsCleanly()
    {
        // Arrange: a host that accepts exactly one connection. A PeerConnected handler disposes the
        // accepted peer (a fire-and-forget access-control gesture) before returning.
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();

        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? acceptError = null;

        await using var host = RpcHost.Listen(
            new SingleConnectionServerTransport(serverConnection), NewSerializer());

        host.AcceptError += (_, args) => acceptError = args.Error;
        host.PeerConnected += (_, args) =>
        {
            // RpcPeer.DisposeAsync sets _disposed synchronously inside its lifecycle lock, so by the
            // time this handler returns the peer is disposed and the host's subsequent peer.Start()
            // would throw ObjectDisposedException on the unfixed code.
            _ = args.Peer.DisposeAsync();
            connected.TrySetResult(true);
        };

        await host.StartAsync().WaitAsync(Timeout5s);

        // Act: connect a client so the host accepts the connection and raises PeerConnected.
        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = Timeout5s });

        Assert.True(await connected.Task.WaitAsync(Timeout10s));

        // StopAsync drains the in-flight hand-off (DrainInFlightAsync). The hand-off's catch raises
        // AcceptError synchronously before the hand-off task completes, so once StopAsync returns the
        // buggy AcceptError would already have fired. This makes the negative assertion deterministic.
        await host.StopAsync().WaitAsync(Timeout10s);

        // Assert: disposing the peer inside PeerConnected is a normal application action; it must NOT
        // surface as an AcceptError (whose contract is transport-accept failures only). On the unfixed
        // code AcceptError fires with an ObjectDisposedException here -> RED.
        var spurious = acceptError;
        Assert.True(
            spurious is null,
            spurious is null
                ? "AcceptError must not be raised when a PeerConnected handler disposes the peer."
                : $"AcceptError was raised spuriously with {spurious.GetType().Name}: {spurious.Message}");
    }
}
