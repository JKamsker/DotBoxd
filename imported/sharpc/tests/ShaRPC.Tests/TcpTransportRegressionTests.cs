using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;
using ShaRPC.Transports.Tcp;
using Xunit;

namespace ShaRPC.Tests;

/// <summary>
/// Regression tests for the TCP transport review findings: cancelling a single accept must not tear
/// down the shared listener (M7), and a sub-header length prefix must be rejected before renting a
/// frame buffer (L15).
/// </summary>
public sealed class TcpTransportRegressionTests
{
    [Fact]
    public async Task AcceptAsync_CanceledForOneCall_DoesNotTearDownListener()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync();
        var port = server.LocalEndpoint?.Port ?? throw new InvalidOperationException("no bound port");

        // Cancel a single accept while no client is connecting. The old implementation Stop()-ed the
        // listener here, breaking every later accept; the fix must leave the listener intact.
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server.AcceptAsync(cts.Token));
        }

        // A subsequent accept still works.
        using var client = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var accepted = await acceptTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(accepted.IsConnected);
    }

    [Fact]
    public async Task ReceiveAsync_RejectsSubHeaderLengthPrefix()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync();
        var port = server.LocalEndpoint?.Port ?? throw new InvalidOperationException("no bound port");

        using var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port);
        await using var serverConnection = await acceptTask.WaitAsync(TimeSpan.FromSeconds(2));

        // A length prefix below the minimum frame size must be rejected with a clear protocol error
        // (not an ArgumentOutOfRangeException from slicing an undersized rented buffer, which also leaked it).
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, 2);
        await rawClient.GetStream().WriteAsync(prefix);
        await rawClient.GetStream().FlushAsync();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => serverConnection.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task RemoteEndpoint_RemainsReadableAfterDispose()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync();
        var port = server.LocalEndpoint?.Port ?? throw new InvalidOperationException("no bound port");

        using var client = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var accepted = await acceptTask.WaitAsync(TimeSpan.FromSeconds(2));

        var before = accepted.RemoteEndpoint;
        Assert.NotEqual("unknown", before);

        await accepted.DisposeAsync();

        // Reading the endpoint after dispose must not throw ObjectDisposedException from the now-closed
        // socket; it is captured at construction so logging and Disconnected handlers stay safe.
        var after = accepted.RemoteEndpoint;
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ReceiveAsync_TimesOutStalledFrameBody()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0)
        {
            FrameReadIdleTimeout = TimeSpan.FromMilliseconds(200),
        };
        await server.StartAsync();
        var port = server.LocalEndpoint?.Port ?? throw new InvalidOperationException("no bound port");

        using var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port);
        await using var serverConnection = await acceptTask.WaitAsync(TimeSpan.FromSeconds(2));

        // Declare a 4 KB frame, then send no body: a slow-loris peer must not pin the connection and
        // the rented buffer indefinitely. The in-progress body read times out with an IOException.
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, 4096);
        await rawClient.GetStream().WriteAsync(prefix);
        await rawClient.GetStream().FlushAsync();

        await Assert.ThrowsAsync<IOException>(
            () => serverConnection.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task StartAsync_AfterStopAsync_RestartsSuccessfully()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);

        await server.StartAsync();
        await server.StopAsync();

        // StopAsync must reset the started flag and listener so the transport can be started again.
        // The pre-fix StopAsync left _started true and StartAsync threw "Server already started.".
        await server.StartAsync();
        var port = server.LocalEndpoint?.Port ?? throw new InvalidOperationException("no bound port");

        using var client = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var accepted = await acceptTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(accepted.IsConnected);
    }

    [Fact]
    public async Task AcceptAsync_AfterStopAsync_ThrowsNotStarted()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync();
        await server.StopAsync();

        // After StopAsync the listener is cleared, so AcceptAsync must surface "not started" rather
        // than accepting on a stopped listener.
        await Assert.ThrowsAsync<InvalidOperationException>(() => server.AcceptAsync());
    }

    [Fact]
    public async Task StopAsync_CalledRepeatedly_IsSafe()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync();

        await server.StopAsync();
        await server.StopAsync();
        await server.StopAsync();
    }

    [Fact]
    public async Task SendAsync_RejectsMismatchedFrameLength()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync();
        var port = server.LocalEndpoint?.Port ?? throw new InvalidOperationException("no bound port");

        using var client = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var accepted = await acceptTask.WaitAsync(TimeSpan.FromSeconds(2));

        // The TCP send path must validate outgoing frames locally (like StreamConnection) instead of
        // shipping a malformed frame to the peer.
        var bad = new byte[MessageFramer.HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(bad, bad.Length + 1);

        await Assert.ThrowsAsync<InvalidDataException>(() => accepted.SendAsync(bad));
    }

    [Fact]
    public async Task ConnectAsync_RefusedPort_LeavesTransportDisconnected()
    {
        var deadPort = ReserveThenReleasePort();
        await using var transport = new TcpTransport(IPAddress.Loopback.ToString(), deadPort);

        await Assert.ThrowsAnyAsync<Exception>(() => transport.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5)));

        // A failed connect must dispose its client and leave no half-open connection behind.
        Assert.False(transport.IsConnected);
        Assert.Null(transport.Connection);
    }

    [Fact]
    public async Task DisposeAsync_AfterFailedConnect_IsCleanAndBlocksReconnect()
    {
        var deadPort = ReserveThenReleasePort();
        var transport = new TcpTransport(IPAddress.Loopback.ToString(), deadPort);

        await Assert.ThrowsAnyAsync<Exception>(() => transport.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5)));

        // Disposing after a failed connect must not throw (atomic dispose tolerates the failed state).
        await transport.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.ConnectAsync());
    }

    private static int ReserveThenReleasePort()
    {
        // Bind a listener to claim a free port, then stop it so connecting to that port is refused.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
