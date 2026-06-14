using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;
using ShaRPC.Tests;
using ShaRPC.Transports.NamedPipes;
using Xunit;

namespace ShaRPC.Tests.Cov.Pipes;

/// <summary>
/// Behavioral coverage for the named-pipe transports, the public single-connection transports, and
/// <see cref="StreamConnection"/>. Every scenario asserts observable behavior (return values, thrown
/// exception types/messages, frame bytes, connection state) and reaches the targeted code purely
/// through the public transport surface (no reflection, no internals access). Existing in-assembly
/// helpers (<see cref="ScriptedConnection"/>, <see cref="InMemoryPipe"/>) are reused where useful.
/// </summary>
public sealed class NamedPipeClientTransportCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static string CreatePipeName() => "sharpc-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void IsConnected_ReturnsFalse_BeforeConnect()
    {
        var transport = new NamedPipeClientTransport(CreatePipeName());

        Assert.False(transport.IsConnected);
        Assert.Null(transport.Connection);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenPipeNameBlank(string pipeName)
    {
        var ex = Assert.Throws<ArgumentException>(() => new NamedPipeClientTransport(pipeName));
        Assert.Equal("pipeName", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenServerNameBlank(string serverName)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new NamedPipeClientTransport(serverName, "some-pipe"));
        Assert.Equal("serverName", ex.ParamName);
    }

    [Fact]
    public void Constructor_Throws_WhenMaxMessageSizeBelowHeader()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new NamedPipeClientTransport(CreatePipeName(), MessageFramer.HeaderSize - 1));
        Assert.Equal("maxMessageSize", ex.ParamName);
    }

    [Fact]
    public async Task ConnectAsync_Throws_WhenAlreadyConnected()
    {
        var pipeName = CreatePipeName();
        await using var serverTransport = new NamedPipeServerTransport(pipeName);
        await serverTransport.StartAsync();
        var acceptTask = serverTransport.AcceptAsync();

        await using var clientTransport = new NamedPipeClientTransport(pipeName);
        await clientTransport.ConnectAsync().WaitAsync(Timeout);
        await using var serverConnection = await acceptTask.WaitAsync(Timeout);

        Assert.True(clientTransport.IsConnected);
        Assert.NotNull(clientTransport.Connection);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => clientTransport.ConnectAsync());
        Assert.Contains("Already connected", ex.Message);
    }

    [Fact]
    public async Task ConnectAsync_Throws_WhenCancelledWithNoServer()
    {
        // No server is listening on this pipe, so ConnectAsync blocks; cancelling it must surface as
        // a cancellation and dispose the underlying stream (the catch/cleanup path).
        await using var transport = new NamedPipeClientTransport(CreatePipeName());
        using var cts = new CancellationTokenSource();
        var connectTask = transport.ConnectAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connectTask.WaitAsync(Timeout));

        Assert.False(transport.IsConnected);
        Assert.Null(transport.Connection);
    }

    [Fact]
    public async Task ConnectAsync_Throws_WhenAlreadyDisposed()
    {
        var transport = new NamedPipeClientTransport(CreatePipeName());
        await transport.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.ConnectAsync());
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent_WhenNeverConnected()
    {
        var transport = new NamedPipeClientTransport(CreatePipeName());

        // Second dispose hits the Interlocked early-return branch.
        await transport.DisposeAsync();
        await transport.DisposeAsync();

        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task DisposeAsync_ClosesConnection_WhenConnected()
    {
        var pipeName = CreatePipeName();
        await using var serverTransport = new NamedPipeServerTransport(pipeName);
        await serverTransport.StartAsync();
        var acceptTask = serverTransport.AcceptAsync();

        var clientTransport = new NamedPipeClientTransport(pipeName);
        await clientTransport.ConnectAsync().WaitAsync(Timeout);
        await using var serverConnection = await acceptTask.WaitAsync(Timeout);

        var connection = clientTransport.Connection!;
        Assert.True(connection.IsConnected);

        await clientTransport.DisposeAsync();

        Assert.False(connection.IsConnected);
    }

    [Fact]
    public async Task RoundTrip_SendsFrameClientToServer_AndBack()
    {
        var pipeName = CreatePipeName();
        await using var serverTransport = new NamedPipeServerTransport(pipeName);
        await serverTransport.StartAsync();
        var acceptTask = serverTransport.AcceptAsync();

        await using var clientTransport = new NamedPipeClientTransport(pipeName);
        await clientTransport.ConnectAsync().WaitAsync(Timeout);
        await using var serverConnection = await acceptTask.WaitAsync(Timeout);
        var clientConnection = clientTransport.Connection!;

        // client -> server
        using var toServer = MessageFramer.FrameToPayload(7, MessageType.Request, new byte[] { 1, 2, 3 });
        var serverReceive = serverConnection.ReceiveAsync();
        await clientConnection.SendAsync(toServer.Memory).WaitAsync(Timeout);
        using var gotByServer = await serverReceive.WaitAsync(Timeout);
        Assert.Equal(toServer.Memory.ToArray(), gotByServer.Memory.ToArray());

        // server -> client
        using var toClient = MessageFramer.FrameToPayload(7, MessageType.Response, new byte[] { 9, 8 });
        var clientReceive = clientConnection.ReceiveAsync();
        await serverConnection.SendAsync(toClient.Memory).WaitAsync(Timeout);
        using var gotByClient = await clientReceive.WaitAsync(Timeout);
        Assert.Equal(toClient.Memory.ToArray(), gotByClient.Memory.ToArray());

        Assert.Equal($"pipe://./{pipeName}", clientConnection.RemoteEndpoint);
    }
}

public sealed class NamedPipeServerTransportCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static string CreatePipeName() => "sharpc-test-" + Guid.NewGuid().ToString("N");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenPipeNameBlank(string pipeName)
    {
        var ex = Assert.Throws<ArgumentException>(() => new NamedPipeServerTransport(pipeName));
        Assert.Equal("pipeName", ex.ParamName);
    }

    [Fact]
    public void Constructor_Throws_WhenMaxAllowedInstancesZero()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new NamedPipeServerTransport(CreatePipeName(), maxAllowedServerInstances: 0));
        Assert.Equal("value", ex.ParamName);
    }

    [Fact]
    public void Constructor_Throws_WhenMaxMessageSizeBelowHeader()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new NamedPipeServerTransport(
                CreatePipeName(),
                maxMessageSize: MessageFramer.HeaderSize - 1));
        Assert.Equal("value", ex.ParamName);
    }

    [Fact]
    public async Task StartAsync_Throws_WhenAlreadyStarted()
    {
        await using var server = new NamedPipeServerTransport(CreatePipeName());
        await server.StartAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync());
        Assert.Contains("already started", ex.Message);
    }

    [Fact]
    public async Task StartAsync_Throws_WhenCancellationRequested()
    {
        await using var server = new NamedPipeServerTransport(CreatePipeName());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server.StartAsync(cts.Token));
    }

    [Fact]
    public async Task StartAsync_Throws_WhenDisposed()
    {
        var server = new NamedPipeServerTransport(CreatePipeName());
        await server.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.StartAsync());
    }

    [Fact]
    public async Task AcceptAsync_Throws_WhenNotStarted()
    {
        await using var server = new NamedPipeServerTransport(CreatePipeName());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => server.AcceptAsync());
        Assert.Contains("not started", ex.Message);
    }

    [Fact]
    public async Task AcceptAsync_Throws_WhenDisposed()
    {
        var server = new NamedPipeServerTransport(CreatePipeName());
        await server.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.AcceptAsync());
    }

    [Fact]
    public async Task AcceptAsync_Throws_WhenSecondPendingAcceptStarted()
    {
        // The transport only supports a single pending accept. AcceptAsync runs synchronously through
        // SetPendingStream before it yields at WaitForConnectionAsync, so by the time the first
        // AcceptAsync() call returns its task the pending slot is already claimed. A second accept
        // must therefore reject deterministically with InvalidOperationException.
        await using var server = new NamedPipeServerTransport(CreatePipeName());
        await server.StartAsync();

        var firstAccept = server.AcceptAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => server.AcceptAsync());
        Assert.Contains("one pending", ex.Message);

        // Tear down the still-pending first accept deterministically.
        await server.StopAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstAccept.WaitAsync(Timeout));
    }

    [Fact]
    public async Task AcceptAsync_Throws_WhenStoppedWhilePending()
    {
        await using var server = new NamedPipeServerTransport(CreatePipeName());
        await server.StartAsync();
        var acceptTask = server.AcceptAsync();

        // Stopping cancels the linked token; the pending WaitForConnectionAsync must surface
        // as cancellation rather than a return.
        await server.StopAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acceptTask.WaitAsync(Timeout));
    }

    [Fact]
    public async Task AcceptAsync_Throws_WhenCallerTokenCancelledWhilePending()
    {
        await using var server = new NamedPipeServerTransport(CreatePipeName());
        await server.StartAsync();
        using var cts = new CancellationTokenSource();
        var acceptTask = server.AcceptAsync(cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acceptTask.WaitAsync(Timeout));
    }

    [Fact]
    public async Task StopAsync_Throws_WhenCancellationRequested()
    {
        await using var server = new NamedPipeServerTransport(CreatePipeName());
        await server.StartAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server.StopAsync(cts.Token));
    }

    [Fact]
    public async Task StopAsync_IsNoOp_WhenNotStarted()
    {
        await using var server = new NamedPipeServerTransport(CreatePipeName());

        // Never started: StopAsync hits the "not started" early-return branch and completes.
        await server.StopAsync();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var server = new NamedPipeServerTransport(CreatePipeName());
        await server.StartAsync();

        await server.DisposeAsync();
        await server.DisposeAsync();

        // After dispose, accept must fail with ObjectDisposed (not hang or accept).
        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.AcceptAsync());
    }

    [Fact]
    public async Task AcceptAsync_AcceptsMultipleConnectionsSequentially()
    {
        var pipeName = CreatePipeName();
        await using var server = new NamedPipeServerTransport(pipeName);
        await server.StartAsync();

        for (var i = 0; i < 3; i++)
        {
            var acceptTask = server.AcceptAsync();
            await using var client = new NamedPipeClientTransport(pipeName);
            await client.ConnectAsync().WaitAsync(Timeout);
            await using var serverConnection = await acceptTask.WaitAsync(Timeout);

            Assert.True(serverConnection.IsConnected);
            Assert.Equal($"pipe://./{pipeName}", serverConnection.RemoteEndpoint);
        }
    }
}

public sealed class SingleConnectionTransportCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Constructor_Throws_WhenConnectionNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new SingleConnectionTransport(connection: null!));
        Assert.Equal("connection", ex.ParamName);
    }

    [Fact]
    public async Task ConnectAsync_CompletesImmediately_AndExposesConnection()
    {
        await using var channel = new ScriptedConnection();
        await using var transport = new SingleConnectionTransport(channel);

        await transport.ConnectAsync().WaitAsync(Timeout);

        Assert.Same(channel, transport.Connection);
        Assert.True(transport.IsConnected);
    }

    [Fact]
    public async Task ConnectionAndIsConnected_BecomeFalse_AfterDispose()
    {
        var channel = new ScriptedConnection();
        var transport = new SingleConnectionTransport(channel, ownsConnection: false);

        await transport.DisposeAsync();

        Assert.Null(transport.Connection);
        Assert.False(transport.IsConnected);

        // Not owned: the channel itself must remain usable.
        Assert.True(channel.IsConnected);
        await channel.DisposeAsync();
    }

    [Fact]
    public async Task ConnectAsync_Throws_AfterDispose()
    {
        await using var channel = new ScriptedConnection();
        var transport = new SingleConnectionTransport(channel);
        await transport.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.ConnectAsync());
    }

    [Fact]
    public async Task DisposeAsync_DisposesConnection_WhenOwned()
    {
        var channel = new TrackingChannel();
        var transport = new SingleConnectionTransport(channel, ownsConnection: true);

        await transport.DisposeAsync();

        Assert.Equal(1, channel.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotDisposeConnection_WhenNotOwned()
    {
        var channel = new TrackingChannel();
        var transport = new SingleConnectionTransport(channel, ownsConnection: false);

        await transport.DisposeAsync();

        Assert.Equal(0, channel.DisposeCount);
        await channel.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent_WhenOwned()
    {
        var channel = new TrackingChannel();
        var transport = new SingleConnectionTransport(channel, ownsConnection: true);

        await transport.DisposeAsync();
        await transport.DisposeAsync();

        Assert.Equal(1, channel.DisposeCount);
    }
}

public sealed class SingleConnectionServerTransportCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Constructor_Throws_WhenConnectionNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new SingleConnectionServerTransport(connection: null!));
        Assert.Equal("connection", ex.ParamName);
    }

    [Fact]
    public async Task AcceptAsync_Throws_WhenNotStarted()
    {
        await using var channel = new ScriptedConnection();
        await using var server = new SingleConnectionServerTransport(channel);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => server.AcceptAsync());
        Assert.Contains("not been started", ex.Message);
    }

    [Fact]
    public async Task StartAsync_Throws_WhenDisposed()
    {
        await using var channel = new ScriptedConnection();
        var server = new SingleConnectionServerTransport(channel);
        await server.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.StartAsync());
    }

    [Fact]
    public async Task AcceptAsync_Throws_WhenDisposed()
    {
        await using var channel = new ScriptedConnection();
        var server = new SingleConnectionServerTransport(channel);
        await server.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.AcceptAsync());
    }

    [Fact]
    public async Task AcceptAsync_ReturnsConnection_OnFirstCall()
    {
        await using var channel = new ScriptedConnection();
        await using var server = new SingleConnectionServerTransport(channel);
        await server.StartAsync();

        var accepted = await server.AcceptAsync().WaitAsync(Timeout);

        Assert.Same(channel, accepted);
    }

    [Fact]
    public async Task AcceptAsync_Blocks_OnSecondCall_UntilStopped()
    {
        await using var channel = new ScriptedConnection();
        await using var server = new SingleConnectionServerTransport(channel);
        await server.StartAsync();

        var first = await server.AcceptAsync().WaitAsync(Timeout);
        Assert.Same(channel, first);

        var secondAccept = server.AcceptAsync();
        // The second accept parks on the stop signal; it must not complete on its own.
        var raced = await Task.WhenAny(secondAccept, Task.Delay(200));
        Assert.NotSame(secondAccept, raced);

        await server.StopAsync();

        // StopAsync sets the result; AcceptAsync then re-checks the token (not cancelled) and throws.
        await Assert.ThrowsAsync<OperationCanceledException>(() => secondAccept.WaitAsync(Timeout));
    }

    [Fact]
    public async Task AcceptAsync_Throws_WhenSecondCallTokenCancelled()
    {
        await using var channel = new ScriptedConnection();
        await using var server = new SingleConnectionServerTransport(channel);
        await server.StartAsync();

        _ = await server.AcceptAsync().WaitAsync(Timeout);

        using var cts = new CancellationTokenSource();
        var secondAccept = server.AcceptAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => secondAccept.WaitAsync(Timeout));
    }

    [Fact]
    public async Task AcceptAsync_Unblocks_WhenDisposedWhilePending()
    {
        var channel = new TrackingChannel();
        var server = new SingleConnectionServerTransport(channel, ownsConnection: true);
        await server.StartAsync();
        _ = await server.AcceptAsync().WaitAsync(Timeout);

        var secondAccept = server.AcceptAsync();
        await server.DisposeAsync();

        // Dispose sets the stop result; the parked accept resumes and throws (token not cancelled).
        await Assert.ThrowsAsync<OperationCanceledException>(() => secondAccept.WaitAsync(Timeout));
        Assert.Equal(1, channel.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotDisposeConnection_WhenNotOwned()
    {
        var channel = new TrackingChannel();
        var server = new SingleConnectionServerTransport(channel, ownsConnection: false);
        await server.StartAsync();

        await server.DisposeAsync();

        Assert.Equal(0, channel.DisposeCount);
        await channel.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent_WhenOwned()
    {
        var channel = new TrackingChannel();
        var server = new SingleConnectionServerTransport(channel, ownsConnection: true);
        await server.StartAsync();

        await server.DisposeAsync();
        await server.DisposeAsync();

        Assert.Equal(1, channel.DisposeCount);
    }
}

/// <summary>
/// Minimal <see cref="IRpcChannel"/> that only tracks how many times it was disposed, so ownership
/// semantics of the single-connection transports can be asserted without a real link.
/// </summary>
internal sealed class TrackingChannel : IRpcChannel
{
    private int _disposeCount;

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public bool IsConnected => DisposeCount == 0;

    public string RemoteEndpoint => "tracking://channel";

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
        Task.FromResult(Payload.Empty);

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCount);
        return default;
    }
}
