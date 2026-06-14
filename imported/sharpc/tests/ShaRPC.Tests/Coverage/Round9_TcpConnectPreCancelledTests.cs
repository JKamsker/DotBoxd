using System.Net;
using ShaRPC.Transports.Tcp;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round 9 regression for <see cref="TcpTransport.ConnectAsync"/>. It had no
/// <c>ct.ThrowIfCancellationRequested()</c> at entry — unlike every sibling entry point
/// (<c>TcpServerTransport.StartAsync</c>/<c>AcceptAsync</c>, <c>NamedPipeClientTransport.ConnectAsync</c>).
/// Its only token check sits inside the branch where the internal <c>Task.WhenAny</c> resolves on the
/// cancelled delay; against a listening server on loopback the connect task wins the race (both tasks are
/// already complete, so WhenAny returns the first argument), so a pre-cancelled token connects anyway. The
/// token must be honoured at entry.
/// </summary>
public sealed class Round9_TcpConnectPreCancelledTests
{
    private static readonly TimeSpan Timeout5s = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ConnectAsync_WithPreCancelledToken_AgainstListeningServer_Throws_AndDoesNotConnect()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync();
        var accept = server.AcceptAsync(); // ready the backlog so the client connect completes promptly
        var port = server.LocalEndpoint!.Port;

        await using var client = new TcpTransport(IPAddress.Loopback.ToString(), port);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // A pre-cancelled token must be honoured at entry, regardless of whether the loopback connect wins
        // the internal WhenAny race.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ConnectAsync(cts.Token).WaitAsync(Timeout5s));

        Assert.False(client.IsConnected);

        // Tidy up the server-side accept (it faults once the server is stopped).
        await server.DisposeAsync();
        try
        {
            await accept;
        }
        catch
        {
            // expected on shutdown
        }
    }
}
