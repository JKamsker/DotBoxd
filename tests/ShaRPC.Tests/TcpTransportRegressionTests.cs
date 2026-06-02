using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
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

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => serverConnection.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2)));
    }
}
