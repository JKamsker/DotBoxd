using System.Net;
using System.Net.Sockets;
using ShaRPC.Transports.Tcp;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// RED regression for DEFECT #3: <see cref="TcpServerTransport.AcceptAsync"/> orphans a FRESH accept when
/// the token cancels concurrently — in the window between starting
/// <c>listener.AcceptTcpClientAsync()</c> and the in-body <c>if (ct.IsCancellationRequested)</c> check.
///
/// Round-2 #4 fixed the PRE-cancelled case (a top-of-method <c>ThrowIfCancellationRequested</c>). But when
/// the token is NOT cancelled at entry, a fresh accept is started; if the token is then cancelled before
/// the in-body cancellation check, that block re-stashes <c>acceptTask</c> only <c>if (claimed is not
/// null)</c>. For the fresh-accept path (<c>claimed == null</c>) the in-flight accept is neither stashed
/// nor observed by <c>ObservePendingAccept</c> — it (and any <see cref="TcpClient"/> the OS delivers) is
/// orphaned/leaked.
///
/// The correct behaviour is to re-stash the in-flight accept unconditionally on concurrent cancellation,
/// so the shutdown observation path can reclaim it. The fix removes the <c>if (claimed is not null)</c>
/// guard from the in-body cancellation block.
///
/// Deterministic via the internal seam <c>_onFreshAcceptStartedForTest</c>, which fires right after the
/// fresh accept is started and before the in-body cancellation check. The test cancels the CTS inside that
/// seam, so the in-body check observes cancellation deterministically with no sleeps or timing races.
/// </summary>
public sealed class Round3_TcpServerConcurrentCancelFreshAcceptTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AcceptAsync_TokenCancelsAfterFreshAcceptStarts_ReStashesFreshAcceptForReclaim()
    {
        // Arrange: a started server with an EMPTY stash, so AcceptAsync takes the fresh-accept path.
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);

        using var cts = new CancellationTokenSource();

        // Seam: cancel the token in the exact window between starting the fresh accept and the in-body
        // IsCancellationRequested check. The token is NOT cancelled at entry, so AcceptAsync passes the
        // top-of-method guard, claims null (empty stash), starts a fresh accept, and only THEN cancels.
        server._onFreshAcceptStartedForTest = () => cts.Cancel();

        // Act: cancellation must surface as OperationCanceledException.
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => server.AcceptAsync(cts.Token).WaitAsync(Timeout));

        // Assert: the fresh in-flight accept must have been re-stashed so the shutdown observation path
        // can reclaim (and dispose any socket it completes with) it.
        // RED today: the in-body cancellation block re-stashes only when claimed is not null, so the
        // fresh accept (claimed == null) is dropped on the floor and Reclaim returns null.
        Task<TcpClient>? reclaimed = server.ReclaimPendingAcceptForTest();
        Assert.NotNull(reclaimed);

        // Observe/dispose the reclaimed in-flight accept so the test does not leak a socket. Stopping the
        // listener faults the pending accept; await-and-ignore here drives that observation to completion.
        await server.StopAsync().WaitAsync(Timeout);
        try
        {
            TcpClient? client = await reclaimed!.WaitAsync(Timeout);
            client?.Dispose();
        }
        catch
        {
            // The accept was faulted by Stop() (expected) or otherwise torn down — nothing to clean up.
        }
    }
}
