using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;
using ShaRPC.Transports.Tcp;
using Xunit;

namespace ShaRPC.Tests.Cov.Tcp;

/// <summary>
/// Behavioral coverage for the real loopback TCP transport over 127.0.0.1:0. Exercises the client
/// <see cref="TcpTransport"/> connect/dispose lifecycle, the <see cref="TcpServerTransport"/>
/// constructors / start / accept / dispose lifecycle, and the framed-message read/write path of
/// <see cref="TcpConnection"/> (including fragmentation, mid-frame disconnect, oversized prefixes,
/// and idle-timeout validation). Every await is bounded by a timeout so a regression fails fast
/// instead of hanging CI.
/// </summary>
public sealed class TcpTransportCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // ---- TcpTransport (client) -------------------------------------------------------------

    [Fact]
    public async Task ConnectAsync_CalledTwice_ThrowsAlreadyConnected()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);

        await using var transport = new TcpTransport("127.0.0.1", port);
        var acceptTask = server.AcceptAsync();
        await transport.ConnectAsync().WaitAsync(Timeout);
        await using var accepted = await acceptTask.WaitAsync(Timeout);

        // The second connect must reject rather than overwrite the live connection.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.ConnectAsync().WaitAsync(Timeout));
        Assert.Contains("Already connected", ex.Message);

        Assert.True(transport.IsConnected);
        Assert.NotNull(transport.Connection);
    }

    [Fact]
    public async Task ConnectAsync_TokenAlreadyCanceled_ThrowsOperationCanceled()
    {
        // Connect to a port that will never accept (refused/black-holed) with a pre-canceled token so
        // the WhenAny race resolves on the canceled delay branch, hitting the cancellation observe path.
        var deadPort = ReserveThenReleasePort();
        await using var transport = new TcpTransport("127.0.0.1", deadPort);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.ConnectAsync(cts.Token).WaitAsync(Timeout));

        // A canceled connect must not publish a half-open connection.
        Assert.False(transport.IsConnected);
        Assert.Null(transport.Connection);
    }

    [Fact]
    public async Task ConnectAsync_CanceledMidConnect_ThrowsAndLeavesDisconnected()
    {
        // Use a routable-but-non-listening address so the connect stays pending, then cancel it. This
        // drives the "WhenAny did not complete with connectTask" branch and ObserveFault.
        var deadPort = ReserveThenReleasePort();
        await using var transport = new TcpTransport("127.0.0.1", deadPort);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<Exception>(
            () => transport.ConnectAsync(cts.Token).WaitAsync(Timeout));

        Assert.False(transport.IsConnected);
        Assert.Null(transport.Connection);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_IsIdempotent()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);

        var transport = new TcpTransport("127.0.0.1", port);
        var acceptTask = server.AcceptAsync();
        await transport.ConnectAsync().WaitAsync(Timeout);
        await using var accepted = await acceptTask.WaitAsync(Timeout);

        await transport.DisposeAsync();
        // Second dispose must short-circuit on the disposed flag and not throw.
        await transport.DisposeAsync();

        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var transport = new TcpTransport("127.0.0.1", 1);
        await transport.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => transport.ConnectAsync().WaitAsync(Timeout));
    }

    [Fact]
    public void Constructor_NullHost_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new TcpTransport(null!, 1234));
    }

    // ---- End-to-end framed send/receive over loopback -------------------------------------

    [Fact]
    public async Task SendReceive_FramedMessage_RoundTripsBothDirections()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);

        await using var transport = new TcpTransport("127.0.0.1", port);
        var acceptTask = server.AcceptAsync();
        await transport.ConnectAsync().WaitAsync(Timeout);
        await using var serverConn = await acceptTask.WaitAsync(Timeout);
        var clientConn = transport.Connection!;

        // Client -> server.
        using var outbound = BuildFrame(messageId: 7, type: 1, bodyLength: 32);
        await clientConn.SendAsync(outbound.Memory).WaitAsync(Timeout);
        using var received = await serverConn.ReceiveAsync().WaitAsync(Timeout);
        Assert.True(received.Span.SequenceEqual(outbound.Span));

        // Server -> client (responses flow back over the same duplex channel).
        using var reply = BuildFrame(messageId: 99, type: 2, bodyLength: 8);
        await serverConn.SendAsync(reply.Memory).WaitAsync(Timeout);
        using var replyRecv = await clientConn.ReceiveAsync().WaitAsync(Timeout);
        Assert.True(replyRecv.Span.SequenceEqual(reply.Span));
    }

    [Fact]
    public async Task ReceiveAsync_FrameFragmentedAcrossReads_Reassembles()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);

        using var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        await using var serverConn = await acceptTask.WaitAsync(Timeout);

        using var frame = BuildFrame(messageId: 5, type: 3, bodyLength: 200);
        var bytes = frame.Span.ToArray();
        var stream = rawClient.GetStream();

        // Hand the bytes to the OS in three separate, separately-flushed writes so the receiver's
        // ReadExact loop must stitch the length prefix + body back together across TCP reads.
        var receiveTask = serverConn.ReceiveAsync();
        await stream.WriteAsync(bytes.AsMemory(0, 2)).AsTask().WaitAsync(Timeout);
        await stream.FlushAsync().WaitAsync(Timeout);
        await stream.WriteAsync(bytes.AsMemory(2, 10)).AsTask().WaitAsync(Timeout);
        await stream.FlushAsync().WaitAsync(Timeout);
        await stream.WriteAsync(bytes.AsMemory(12)).AsTask().WaitAsync(Timeout);
        await stream.FlushAsync().WaitAsync(Timeout);

        using var received = await receiveTask.WaitAsync(Timeout);
        Assert.True(received.Span.SequenceEqual(frame.Span));
    }

    [Fact]
    public async Task ReceiveAsync_PeerClosesBeforeAnyData_ReturnsEmpty()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);

        var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        await using var serverConn = await acceptTask.WaitAsync(Timeout);

        // Close the client with no bytes sent: the length-prefix read sees EOF and signals closure.
        rawClient.Close();

        using var received = await serverConn.ReceiveAsync().WaitAsync(Timeout);
        Assert.Equal(0, received.Length);
        Assert.Same(Payload.Empty, received);
    }

    [Fact]
    public async Task ReceiveAsync_PeerClosesMidFrameBody_ReturnsEmpty()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0)
        {
            // Disable the slow-loris timeout so the partial-then-EOF path (short body read) is what
            // ends the receive, not an idle-timeout IOException.
            FrameReadIdleTimeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);

        var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        await using var serverConn = await acceptTask.WaitAsync(Timeout);

        // Declare a 256-byte frame, send only the header + a few body bytes, then close. The body read
        // returns fewer bytes than declared -> the rented payload is disposed and Empty is returned.
        var full = BuildFrame(messageId: 1, type: 1, bodyLength: 256).Span.ToArray();
        var stream = rawClient.GetStream();
        var receiveTask = serverConn.ReceiveAsync();
        await stream.WriteAsync(full.AsMemory(0, 20)).AsTask().WaitAsync(Timeout);
        await stream.FlushAsync().WaitAsync(Timeout);
        rawClient.Close();

        using var received = await receiveTask.WaitAsync(Timeout);
        Assert.Equal(0, received.Length);
    }

    [Fact]
    public async Task ReceiveAsync_OversizedLengthPrefix_ThrowsInvalidData()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);

        using var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        await using var serverConn = await acceptTask.WaitAsync(Timeout);

        // A length prefix beyond MaxMessageSize must be rejected before renting a frame buffer.
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, MessageFramer.MaxMessageSize + 1);
        await rawClient.GetStream().WriteAsync(prefix.AsMemory()).AsTask().WaitAsync(Timeout);
        await rawClient.GetStream().FlushAsync().WaitAsync(Timeout);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => serverConn.ReceiveAsync().WaitAsync(Timeout));
        Assert.Contains("Invalid ShaRPC frame length", ex.Message);
    }

    // ---- TcpConnection direct lifecycle ---------------------------------------------------

    [Fact]
    public async Task SendAsync_AfterDispose_ThrowsObjectDisposed()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);

        using var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        var serverConn = await acceptTask.WaitAsync(Timeout);

        await serverConn.DisposeAsync();

        using var frame = BuildFrame(messageId: 1, type: 1, bodyLength: 4);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => serverConn.SendAsync(frame.Memory).WaitAsync(Timeout));
    }

    [Fact]
    public async Task ReceiveAsync_AfterDispose_ThrowsObjectDisposed()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);

        using var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        var serverConn = await acceptTask.WaitAsync(Timeout);

        await serverConn.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => serverConn.ReceiveAsync().WaitAsync(Timeout));
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_IsIdempotentAndMarksDisconnected()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);

        using var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        var serverConn = await acceptTask.WaitAsync(Timeout);

        Assert.True(serverConn.IsConnected);

        await serverConn.DisposeAsync();
        // Second dispose hits the Interlocked short-circuit and must not throw or double-dispose state.
        await serverConn.DisposeAsync();

        Assert.False(serverConn.IsConnected);
    }

    [Fact]
    public async Task ReceiveAsync_AfterRemotePeerDisconnects_ReturnsEmptyAndDisposeFlipsIsConnected()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);

        var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        await using var serverConn = await acceptTask.WaitAsync(Timeout);

        Assert.True(serverConn.IsConnected);
        Assert.NotEqual("unknown", serverConn.RemoteEndpoint);

        // The remote went away: a receive observes the FIN and returns the documented Empty sentinel.
        // (IsConnected itself is a best-effort hint that does not probe the wire, so a graceful remote
        // FIN does not necessarily flip Socket.Connected on a half-open socket — the receive result is
        // the authoritative disconnect signal, per TcpConnection's contract.)
        rawClient.Close();
        using var received = await serverConn.ReceiveAsync().WaitAsync(Timeout);
        Assert.Equal(0, received.Length);

        // EOF is idempotent: a second receive on the closed connection still returns Empty, never hangs.
        using var received2 = await serverConn.ReceiveAsync().WaitAsync(Timeout);
        Assert.Equal(0, received2.Length);

        // Disposing the connection is the authoritative LOCAL signal and flips the hint to false.
        await serverConn.DisposeAsync();
        Assert.False(serverConn.IsConnected);
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static int RequirePort(TcpServerTransport server) =>
        server.LocalEndpoint?.Port ?? throw new InvalidOperationException("no bound port");

    private static int ReserveThenReleasePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>
    /// Builds a wire frame whose 4-byte little-endian length prefix matches the buffer length, so it
    /// passes <see cref="MessageFramer.ValidateOutgoingFrame"/> on the send path. Layout mirrors a real
    /// ShaRPC frame header (length, messageId, type) followed by arbitrary body bytes.
    /// </summary>
    private static Payload BuildFrame(int messageId, byte type, int bodyLength)
    {
        var total = MessageFramer.HeaderSize + bodyLength;
        var payload = Payload.Rent(total);
        var span = payload.Memory.Span;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), total);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), messageId);
        span[8] = type;
        for (var i = MessageFramer.HeaderSize; i < total; i++)
        {
            span[i] = (byte)(i & 0xFF);
        }

        return payload;
    }
}
