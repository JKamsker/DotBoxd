using System.Net;
using ShaRPC.Transports.Tcp;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// RED regression for finding #4: when <see cref="TcpServerTransport.AcceptAsync"/> is called with an
/// already-cancelled token and the stash is empty, the current code starts a fresh
/// <c>listener.AcceptTcpClientAsync()</c> BEFORE checking the token, then throws
/// <see cref="OperationCanceledException"/> — orphaning that fresh accept (it is never stashed, so the
/// shutdown observation path can never reclaim it, and a client connecting to it leaks a TcpClient).
/// This refines the round-1 #7 fix, which only re-stashed the claimed (non-fresh) case.
///
/// The fix moves the pre-cancellation check to the very top of AcceptAsync, before claiming or starting
/// any accept. Fully deterministic and single-threaded via the internal FreshAcceptStartsForTest counter.
/// </summary>
public sealed class Round2_TcpServerPreCancelledFreshAcceptTests
{
    [Fact]
    public async Task AcceptAsync_PreCancelledToken_EmptyStash_StartsNoFreshAccept()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => server.AcceptAsync(cts.Token));

        // On the unfixed code a fresh AcceptTcpClientAsync was started (and orphaned) before the
        // cancellation check fired -> count == 1 (RED). The fix checks cancellation first -> count == 0.
        Assert.Equal(0, server.FreshAcceptStartsForTest);
    }
}
