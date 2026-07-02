using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.NamedPipes;
using Xunit;
namespace DotBoxD.Services.Tests.Coverage.Transport;

public sealed partial class NamedPipeClientTransportCoverageTests
{
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
    public async Task ConnectAsync_WithPreCancelledToken_ThrowsOperationCanceled()
    {
        await using var channel = new ScriptedConnection();
        await using var transport = new SingleConnectionTransport(channel);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.ConnectAsync(cts.Token));
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

}
