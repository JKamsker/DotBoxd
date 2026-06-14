using System.Net;
using System.Net.Sockets;
using ShaRPC.Transports.Tcp;
using Xunit;

namespace ShaRPC.Tests.Cov.Tcp;

/// <summary>
/// Behavioral coverage for <see cref="TcpServerTransport"/> construction, start/accept/stop/dispose
/// lifecycle, and the cancellation-then-shutdown path that stashes and reclaims an in-flight accept.
/// Also covers <see cref="TcpConnection"/> construction guards (null client, invalid idle timeout)
/// reached through the public transport surface and the public connection constructor. All loopback,
/// port 0, every await bounded by a timeout.
/// </summary>
public sealed class TcpServerTransportCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // ---- Constructors ---------------------------------------------------------------------

    [Fact]
    public async Task Constructor_PortOnly_BindsOnAnyAddress()
    {
        // The (int port) overload delegates to (IPAddress.Any, port). Binding port 0 picks a free port
        // and StartAsync must expose the bound endpoint.
        await using var server = new TcpServerTransport(0);
        await server.StartAsync().WaitAsync(Timeout);

        Assert.NotNull(server.LocalEndpoint);
        Assert.True(server.LocalEndpoint!.Port > 0);
    }

    [Fact]
    public async Task Constructor_StringAddress_ParsesAndBinds()
    {
        // The (string address, int port) overload parses the address via IPAddress.Parse.
        await using var server = new TcpServerTransport("127.0.0.1", 0);
        await server.StartAsync().WaitAsync(Timeout);

        Assert.NotNull(server.LocalEndpoint);
        Assert.Equal(IPAddress.Loopback, server.LocalEndpoint!.Address);
    }

    [Fact]
    public void Constructor_InvalidStringAddress_Throws()
    {
        Assert.ThrowsAny<Exception>(() => new TcpServerTransport("not-an-ip", 0));
    }

    [Fact]
    public void Constructor_NullAddress_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new TcpServerTransport((IPAddress)null!, 0));
    }

    // ---- Start lifecycle ------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_CalledTwice_ThrowsAlreadyStarted()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => server.StartAsync().WaitAsync(Timeout));
        Assert.Contains("already started", ex.Message);
    }

    [Fact]
    public async Task StartAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => server.StartAsync().WaitAsync(Timeout));
    }

    [Fact]
    public async Task StartAsync_PortAlreadyInUse_ResetsStartedFlagAndAllowsRetry()
    {
        // Occupy a port with a raw listener, then try to bind a second server to the same port. The
        // bind throws and StartAsync must reset _started so a later StartAsync on a free port succeeds.
        var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        var busyPort = ((IPEndPoint)occupied.LocalEndpoint).Port;
        try
        {
            await using var server = new TcpServerTransport(IPAddress.Loopback, busyPort);

            await Assert.ThrowsAnyAsync<Exception>(() => server.StartAsync().WaitAsync(Timeout));
            // LocalEndpoint stays null because the listener field was never published on failure.
            Assert.Null(server.LocalEndpoint);

            // _started was reset on the bind failure, so a retry is not blocked by "already started".
            // (Re-binding the same busy port still fails — assert the failure is the bind, not the guard.)
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => server.StartAsync().WaitAsync(Timeout));
            Assert.IsNotType<InvalidOperationException>(ex);
        }
        finally
        {
            occupied.Stop();
        }
    }

    // ---- Accept lifecycle -----------------------------------------------------------------

    [Fact]
    public async Task AcceptAsync_BeforeStart_ThrowsNotStarted()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => server.AcceptAsync().WaitAsync(Timeout));
        Assert.Contains("not started", ex.Message);
    }

    [Fact]
    public async Task AcceptAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        await server.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => server.AcceptAsync().WaitAsync(Timeout));
    }

    [Fact]
    public async Task AcceptAsync_ReturnsConnectedChannel_WhenClientConnects()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = server.LocalEndpoint!.Port;

        using var client = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        await using var accepted = await acceptTask.WaitAsync(Timeout);

        Assert.True(accepted.IsConnected);
        Assert.NotEqual("unknown", accepted.RemoteEndpoint);
    }

    [Fact]
    public async Task AcceptAsync_CanceledThenStopped_DisposesReclaimedClientWithoutLeak()
    {
        // Cancel an accept while no one is connecting so the in-flight accept is stashed in
        // _pendingAccept. Then connect a client (completing the stashed accept with a live socket) and
        // immediately Stop the server. ObservePendingAccept must reclaim and dispose that live socket.
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = server.LocalEndpoint!.Port;

        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => server.AcceptAsync(cts.Token).WaitAsync(Timeout));
        }

        // Connect now so the stashed accept completes with a real TcpClient, then stop.
        using var raceClient = new TcpClient();
        await raceClient.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        await server.StopAsync().WaitAsync(Timeout);

        // After Stop the server is restartable (started flag reset, listener cleared -> not started).
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => server.AcceptAsync().WaitAsync(Timeout));
        Assert.Contains("not started", ex.Message);
    }

    [Fact]
    public async Task AcceptAsync_CanceledThenDisposed_ObservesPendingAcceptFault()
    {
        // Cancel an accept (stashing _pendingAccept) with NO client connecting, then dispose. Disposing
        // stops the listener which faults the stashed accept; ObservePendingAccept observes that fault.
        var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);

        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => server.AcceptAsync(cts.Token).WaitAsync(Timeout));
        }

        // Dispose tears down the listener and reclaims the stashed (now-faulting) accept.
        await server.DisposeAsync();
        await server.DisposeAsync(); // double dispose: Interlocked short-circuit, no throw.
    }

    [Fact]
    public async Task AcceptAsync_CanceledMidWait_RethrowsAsOperationCanceled()
    {
        // No client ever connects; the accept faults under cancellation. The catch-when filter maps the
        // listener fault to OperationCanceledException. We cancel after the accept is already in flight.
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => server.AcceptAsync(cts.Token).WaitAsync(Timeout));
    }

    // ---- Dispose / stop idempotency -------------------------------------------------------

    [Fact]
    public async Task DisposeAsync_CalledTwice_IsIdempotent()
    {
        var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);

        await server.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task StopAsync_WithoutStart_IsSafe()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);

        // StopAsync before StartAsync just resets already-zero state and observes a null pending accept.
        await server.StopAsync().WaitAsync(Timeout);
    }

    // ---- TcpConnection construction guards ------------------------------------------------

    [Fact]
    public void TcpConnection_NullClient_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new TcpConnection(null!));
    }

    [Theory]
    [InlineData(0)]            // zero is not positive
    [InlineData(-5)]           // negative is invalid
    public async Task FrameReadIdleTimeout_NonPositive_ThrowsOnAccept(int millis)
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0)
        {
            FrameReadIdleTimeout = TimeSpan.FromMilliseconds(millis),
        };
        await server.StartAsync().WaitAsync(Timeout);
        var port = server.LocalEndpoint!.Port;

        using var client = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);

        // The invalid idle timeout is validated inside the TcpConnection constructor the accept builds.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => acceptTask.WaitAsync(Timeout));
    }

    [Fact]
    public async Task FrameReadIdleTimeout_ExceedsIntMaxMilliseconds_ThrowsOnAccept()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0)
        {
            // > int.MaxValue milliseconds (~24.8 days) is rejected.
            FrameReadIdleTimeout = TimeSpan.FromDays(30),
        };
        await server.StartAsync().WaitAsync(Timeout);
        var port = server.LocalEndpoint!.Port;

        using var client = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => acceptTask.WaitAsync(Timeout));
    }

    [Fact]
    public async Task FrameReadIdleTimeout_Infinite_AcceptsSuccessfully()
    {
        // Timeout.InfiniteTimeSpan is the explicit "disabled" sentinel and must bypass the range check.
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0)
        {
            FrameReadIdleTimeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
        await server.StartAsync().WaitAsync(Timeout);
        var port = server.LocalEndpoint!.Port;

        using var client = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        await using var accepted = await acceptTask.WaitAsync(Timeout);

        Assert.True(accepted.IsConnected);
    }
}
