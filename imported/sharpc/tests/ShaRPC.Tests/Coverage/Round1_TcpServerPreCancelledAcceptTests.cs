using System.Net;
using System.Net.Sockets;
using ShaRPC.Transports.Tcp;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// RED regression for DEFECT #7: <see cref="TcpServerTransport.AcceptAsync"/> must honour an
/// already-cancelled token even when the accept it acquires is already completed.
///
/// When <c>ClaimPendingAccept</c> returns an already-completed task, the cancellation guard
/// <c>(ct.CanBeCanceled &amp;&amp; !acceptTask.IsCompleted)</c> short-circuits and skips the
/// cancellation block. With no <c>ct.ThrowIfCancellationRequested()</c> before awaiting the
/// completed task, <c>AcceptAsync</c> awaits it and returns a connection instead of throwing
/// <see cref="OperationCanceledException"/> — the <c>catch ... when (ct.IsCancellationRequested)</c>
/// filter only fires on exceptions, and a completed <see cref="Task.FromResult{TResult}"/> does not
/// throw. The correct behaviour is to surface cancellation, matching
/// <c>TcpTransport.ConnectAsync</c>'s handling of pre-cancelled tokens.
///
/// Deterministic and single-threaded: the existing internal seam
/// <c>StashPendingAcceptForTest</c> plants a completed accept; no real connection is needed.
/// </summary>
public sealed class Round1_TcpServerPreCancelledAcceptTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AcceptAsync_PreCancelledToken_WithCompletedStashedAccept_ThrowsOperationCanceled()
    {
        // Arrange: a started server with a completed accept stashed in _pendingAccept, and a token
        // that is already cancelled before AcceptAsync runs.
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);

        using var dummy = new TcpClient();
        server.StashPendingAcceptForTest(Task.FromResult(dummy));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act + Assert: the already-cancelled token must win over the completed stashed accept.
        // RED today: AcceptAsync returns a TcpConnection (built from the dummy client) instead of
        // throwing, because the IsCompleted short-circuit skips the cancellation path.
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => server.AcceptAsync(cts.Token).WaitAsync(Timeout));
    }
}
