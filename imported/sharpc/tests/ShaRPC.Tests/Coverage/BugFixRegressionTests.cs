using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using MessagePack;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.NamedPipes;
using ShaRPC.Transports.Tcp;
using ShaRPC.Tests;
using Xunit;

namespace ShaRPC.Tests.Cov.BugFixes;

/// <summary>
/// Regression tests for the defects found by the adversarial bug hunt and fixed in the library.
/// Each test exercises the corrected behavior through the public API; the racy fixes (#5/#6/#10) are
/// covered via their deterministic functional path since the exact interleaving cannot be forced.
/// </summary>
public sealed class BugFixRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static MessagePackRpcSerializer NewSerializer() => new();

    // -- #1: RpcHost.StartAsync must run under the host's stop-linked token so StopAsync/DisposeAsync
    //        can interrupt a cooperative transport start instead of hanging shutdown. -----------------

    [Fact]
    public async Task RpcHost_StopAsync_InterruptsCooperativelyBlockingListenerStart()
    {
        var transport = new TokenBlockingStartTransport();
        await using var host = RpcHost.Listen(transport, NewSerializer());

        var startTask = host.StartAsync();
        await transport.StartEntered.Task.WaitAsync(Timeout);

        // Before the fix StartAsync awaited the bare caller token (here CancellationToken.None), so
        // cancelling the host's internal token via StopAsync could not unblock the transport — this
        // StopAsync would hang. Now StartAsync runs under the linked token and stop unblocks it.
        await host.StopAsync().WaitAsync(Timeout);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startTask.WaitAsync(Timeout));
        Assert.True(transport.StartObservedCancellation);
    }

    [Fact]
    public async Task RpcHost_DisposeDuringCooperativeStart_StillSurfacesObjectDisposed()
    {
        // The fix preserves the public contract: a start interrupted *because the host was disposed*
        // still throws ObjectDisposedException (not a raw cancellation).
        var transport = new TokenBlockingStartTransport();
        var host = RpcHost.Listen(transport, NewSerializer());

        var startTask = host.StartAsync();
        await transport.StartEntered.Task.WaitAsync(Timeout);

        await host.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => startTask.WaitAsync(Timeout));
    }

    // -- #2: PeerConnected must be raised before the peer's read loop starts, so an immediate remote
    //        close cannot surface PeerDisconnected ahead of PeerConnected for the same peer. ----------

    [Fact]
    public async Task RpcHost_RaisesPeerConnectedBeforePeerDisconnected_OnImmediateRemoteClose()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        // Close the client end first: the accepted server peer's very first receive returns Empty, so
        // its read loop ends (and fires Disconnected) the instant it starts.
        await clientConnection.DisposeAsync();

        var events = new ConcurrentQueue<string>();
        var disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost.Listen(new SingleConnectionServerTransport(serverConnection), NewSerializer());
        host.PeerConnected += (_, _) => events.Enqueue("connected");
        host.PeerDisconnected += (_, _) =>
        {
            events.Enqueue("disconnected");
            disconnected.TrySetResult(true);
        };

        await host.StartAsync();
        await disconnected.Task.WaitAsync(Timeout);

        var ordered = events.ToArray();
        Assert.Equal("connected", ordered[0]);
        Assert.Contains("disconnected", ordered);
        Assert.True(
            Array.IndexOf(ordered, "connected") < Array.IndexOf(ordered, "disconnected"),
            "PeerConnected must precede PeerDisconnected for the same peer");
    }

    // -- #3: NamedPipeServerTransport must coordinate _stopCts so a Stop racing a pending Accept cancels
    //        cleanly (OperationCanceledException) instead of throwing NRE/ObjectDisposedException, and a
    //        later Accept fails fast with "not started". ----------------------------------------------

    [Fact]
    public async Task NamedPipeServerTransport_StopDuringPendingAccept_CancelsCleanlyAndRejectsLaterAccept()
    {
        var pipeName = "sharpc-bugfix-" + Guid.NewGuid().ToString("N");
        var server = new NamedPipeServerTransport(pipeName);
        try
        {
            await server.StartAsync();

            // Park an accept (no client will connect); its linked token is wired to _stopCts.
            var acceptTask = server.AcceptAsync();

            await server.StopAsync();

            // Clean cancellation, never NullReference/ObjectDisposed from racing _stopCts.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acceptTask.WaitAsync(Timeout));

            // After stop _stopCts is null; the next accept fails fast with a clear "not started".
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => server.AcceptAsync());
            Assert.Contains("not started", ex.Message);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    // -- #4: Every transport surfaces the SAME exception type (InvalidDataException, an IOException) for
    //        a malformed inbound frame length. -------------------------------------------------------

    [Fact]
    public async Task MalformedFrameLength_ThrowsInvalidDataException_ConsistentlyAcrossTransports()
    {
        // StreamConnection
        var subHeader = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(subHeader, 2); // below the header size
        await using (var stream = new StreamConnection(new MemoryStream(subHeader)))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => stream.ReceiveAsync());
        }

        // MessageFramer.ReadMessageAsync
        var header = new byte[MessageFramer.HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), 2);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => MessageFramer.ReadMessageAsync(new MemoryStream(header)));

        // TcpConnection over loopback — same malformed length, same exception type.
        await using var listener = new TcpServerTransport(IPAddress.Loopback, 0);
        await listener.StartAsync().WaitAsync(Timeout);
        var port = listener.LocalEndpoint!.Port;
        using var rawClient = new TcpClient();
        var acceptTask = listener.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        await using var serverConn = await acceptTask.WaitAsync(Timeout);

        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, MessageFramer.MaxMessageSize + 1);
        await rawClient.GetStream().WriteAsync(prefix.AsMemory()).AsTask().WaitAsync(Timeout);
        await rawClient.GetStream().FlushAsync().WaitAsync(Timeout);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => serverConn.ReceiveAsync().WaitAsync(Timeout));
    }

    // -- #5: NamedPipeClientTransport disposed-state guard (the post-connect race guard mirrors
    //        TcpTransport; the deterministic entry guard is asserted here). ---------------------------

    [Fact]
    public async Task NamedPipeClientTransport_ConnectAfterDispose_ThrowsObjectDisposed()
    {
        var client = new NamedPipeClientTransport("sharpc-bugfix-" + Guid.NewGuid().ToString("N"));
        await client.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync());
    }

    // -- #6: TcpServerTransport stashes an accept cancelled mid-flight and atomically reuses it on the
    //        next call (the path made race-safe by the Interlocked consume). --------------------------

    [Fact]
    public async Task TcpServerTransport_CancelledAccept_StashesAndReusesInFlightAccept()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = server.LocalEndpoint!.Port;

        // First accept is cancelled before any client connects -> the in-flight accept is stashed.
        using var cts = new CancellationTokenSource();
        var firstAccept = server.AcceptAsync(cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstAccept.WaitAsync(Timeout));

        // Now a client connects; the next accept must reuse the stashed accept and hand back the socket.
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        await using var accepted = await server.AcceptAsync().WaitAsync(Timeout);
        Assert.True(accepted.IsConnected);
    }

    // -- #8/#9: removing the unreachable `totalLength == 4` branch in StreamConnection must not change
    //           handling of the minimal valid (header-only, zero-body) frame at the same boundary. ----

    [Fact]
    public async Task StreamConnection_HeaderOnlyFrame_RoundTripsAfterDeadBranchRemoval()
    {
        // A frame whose declared total length is exactly HeaderSize carries no body and must be returned
        // intact — this is the boundary the removed `totalLength == 4` dead branch sat next to.
        var frame = new byte[MessageFramer.HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), MessageFramer.HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), 42);
        frame[8] = (byte)MessageType.Request;

        await using var connection = new StreamConnection(new MemoryStream(frame));
        using var received = await connection.ReceiveAsync().WaitAsync(Timeout);

        Assert.Equal(MessageFramer.HeaderSize, received.Length);
        Assert.Equal(MessageFramer.HeaderSize, BinaryPrimitives.ReadInt32LittleEndian(received.Memory.Span.Slice(0, 4)));
    }

    // -- #11: MessagePackRpcSerializer.CreateOptions must reject null resolver elements eagerly. -------

    [Fact]
    public void MessagePackRpcSerializer_CreateOptions_NullResolverElement_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => MessagePackRpcSerializer.CreateOptions(new IFormatterResolver[] { null! }));
        Assert.Equal("resolvers", ex.ParamName);
    }

    [Fact]
    public void MessagePackRpcSerializer_CreateOptions_NoResolvers_StillProducesUsableOptions()
    {
        var options = MessagePackRpcSerializer.CreateOptions();
        Assert.NotNull(options);

        // Round-trips through a serializer built on the default options.
        var serializer = new MessagePackRpcSerializer(options);
        using var writer = new PooledBufferWriter();
        serializer.Serialize(writer, 12345);
        using var payload = writer.DetachPayload();
        Assert.Equal(12345, serializer.Deserialize<int>(payload.Memory));
    }

    // ---------------------------------------------------------------------------------------------
    // Test doubles
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Server transport whose StartAsync parks on <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
    /// bound to the token it is given, so it only returns when that token is cancelled. Lets a test
    /// prove the host now starts the listener under its own stop-linked token.
    /// </summary>
    private sealed class TokenBlockingStartTransport : IServerTransport
    {
        private int _cancelled;

        public TaskCompletionSource<bool> StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool StartObservedCancellation => Volatile.Read(ref _cancelled) != 0;

        public async Task StartAsync(CancellationToken ct = default)
        {
            StartEntered.TrySetResult(true);
            try
            {
                await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Exchange(ref _cancelled, 1);
                throw;
            }
        }

        public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default) =>
            Task.FromException<IRpcChannel>(new InvalidOperationException("accept must not run after an interrupted start"));

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;
    }
}
