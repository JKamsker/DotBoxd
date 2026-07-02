using DotBoxD.Services.Protocol;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.NamedPipes;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport;

/// <summary>
/// Behavioral coverage for the named-pipe transports, the public single-connection transports, and
/// <see cref="StreamConnection"/>. Every scenario asserts observable behavior (return values, thrown
/// exception types/messages, frame bytes, connection state) and reaches the targeted code purely
/// through the public transport surface (no reflection, no internals access). Existing in-assembly
/// helpers (<see cref="ScriptedConnection"/>, <see cref="InMemoryPipe"/>) are reused where useful.
/// </summary>
public sealed partial class NamedPipeClientTransportCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static string CreatePipeName() => "dotboxd-test-" + Guid.NewGuid().ToString("N");

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
    public async Task DisposeAsync_CancelsConnectAsync_BeforeStreamPublished()
    {
        var transport = new NamedPipeClientTransport(CreatePipeName());
        using var cleanup = new CancellationTokenSource();
        var connectTask = transport.ConnectAsync(cleanup.Token);

        await transport.DisposeAsync();

        Exception? observed = null;
        try
        {
            observed = await Record.ExceptionAsync(() => connectTask.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            cleanup.Cancel();
            await Record.ExceptionAsync(() => connectTask.WaitAsync(Timeout));
        }

        Assert.True(
            observed is OperationCanceledException or ObjectDisposedException,
            $"Expected disposal to complete the pending connect, but observed {observed?.GetType().Name ?? "success"}.");
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

    private static string CreatePipeName() => "dotboxd-test-" + Guid.NewGuid().ToString("N");

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
        Assert.Equal("maxAllowedServerInstances", ex.ParamName);
    }

    [Fact]
    public void Constructor_Throws_WhenMaxAllowedInstancesExceedsPlatformLimit()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new NamedPipeServerTransport(CreatePipeName(), maxAllowedServerInstances: 255));
        Assert.Equal("maxAllowedServerInstances", ex.ParamName);
    }

    [Fact]
    public void Constructor_Throws_WhenMaxMessageSizeBelowHeader()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new NamedPipeServerTransport(
                CreatePipeName(),
                maxMessageSize: MessageFramer.HeaderSize - 1));
        Assert.Equal("maxMessageSize", ex.ParamName);
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

}
