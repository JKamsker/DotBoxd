using System.Net;
using System.Net.Sockets;
using ShaRPC.Transports.Tcp;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round 5 regressions for <see cref="TcpServerTransport"/>.
///
/// <para>
/// #5 [cancellation]: <c>StartAsync</c> declared a <see cref="CancellationToken"/> but never read it, so a
/// pre-cancelled token bound a live listener instead of throwing — unlike <c>NamedPipeServerTransport</c>,
/// which calls <c>ct.ThrowIfCancellationRequested()</c> first.
/// </para>
///
/// <para>
/// #1 [leak, high]: <c>AcceptAsync</c> constructed <c>new TcpConnection(client, FrameReadIdleTimeout)</c>
/// with no try/catch. The ctor rejects a non-positive timeout (e.g. <see cref="TimeSpan.Zero"/>) AFTER the
/// OS socket is accepted, so the accepted <see cref="TcpClient"/> leaked. Driven by the host accept loop's
/// 50&#160;ms retry, this becomes a socket-exhaustion loop. The accepted client must be disposed on ctor
/// failure (as <c>NamedPipeServerTransport</c> already does).
/// </para>
/// </summary>
public sealed class Round5_TcpServerTransportTests
{
    private static readonly TimeSpan Timeout5s = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StartAsync_WithPreCancelledToken_Throws_AndDoesNotBindListener()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server.StartAsync(cts.Token));
        Assert.Null(server.LocalEndpoint);
    }

    [Fact]
    public async Task AcceptAsync_WhenConnectionConstructionThrows_DisposesAcceptedClient_NoSocketLeak()
    {
        // FrameReadIdleTimeout = Zero makes the TcpConnection ctor throw AFTER the socket is accepted.
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0)
        {
            FrameReadIdleTimeout = TimeSpan.Zero,
        };
        await server.StartAsync();
        var port = server.LocalEndpoint!.Port;

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout5s);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => server.AcceptAsync());

        // Observable proof the server closed its accepted socket: our client's read completes with 0
        // (graceful close) promptly. On the leak the server socket stays open and the read never
        // completes, so WaitAsync times out (RED).
        var buffer = new byte[1];
        var read = await client.GetStream().ReadAsync(buffer, 0, 1).WaitAsync(Timeout5s);
        Assert.Equal(0, read);
    }
}
