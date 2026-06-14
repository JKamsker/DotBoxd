using ShaRPC.Core.Transport;
using ShaRPC.Transports.NamedPipes;
using Xunit;

namespace ShaRPC.Tests.Cov.BugFixes;

/// <summary>
/// DETERMINISTIC red→green regression for DEFECT #6: a client that connects while
/// <see cref="NamedPipeServerTransport.StopAsync"/> runs must never yield a <see cref="StreamConnection"/>
/// over an already-disposed pipe. The race is between <c>WaitForConnectionAsync</c> returning
/// successfully and <c>StopAsync</c> disposing the pending stream before <c>AcceptAsync</c> constructs
/// the channel.
///
/// The window is far smaller than thread-pool/async latency, so this uses the internal
/// <c>_onConnectionEstablishedForTest</c> seam (exposed via InternalsVisibleTo) to force the exact
/// interleaving: the seam fires after <c>WaitForConnectionAsync</c> returns and calls <c>StopAsync</c>,
/// which disposes the pending stream. On the UNFIXED code <c>AcceptAsync</c> still constructs and returns
/// a channel whose <see cref="StreamConnection.IsConnected"/> is false (every Send/Receive throws
/// ObjectDisposedException) with no exception at the accept site — so this test is RED. The fix re-checks
/// cancellation/IsConnected after the wait and throws <see cref="OperationCanceledException"/>, turning it
/// GREEN.
/// </summary>
public sealed class Round1_NamedPipeAcceptDuringStopTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static string CreatePipeName() => "sharpc-round1-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task AcceptAsync_WhenStopDisposesPendingStreamAfterConnect_DoesNotReturnDisconnectedChannel()
    {
        var pipeName = CreatePipeName();
        await using var server = new NamedPipeServerTransport(pipeName);
        await server.StartAsync();

        // Seam: after WaitForConnectionAsync returns (the client below has connected) and before the
        // StreamConnection is constructed, run StopAsync. StopAsync disposes the pending stream and
        // cancels the linked token, recreating the production race deterministically.
        server._onConnectionEstablishedForTest = () => server.StopAsync();

        var acceptTask = server.AcceptAsync();

        await using var client = new NamedPipeClientTransport(pipeName);
        await client.ConnectAsync().WaitAsync(Timeout);

        // Correct/desired behavior: AcceptAsync must surface the stop as cancellation, OR (acceptably)
        // return a channel that is genuinely connected. It must NEVER return a channel over a stream that
        // StopAsync already disposed (IsConnected == false). The unfixed code takes exactly that forbidden
        // path, so the assertion below fails today and passes once the post-wait re-check is added.
        IRpcChannel? accepted = null;
        try
        {
            accepted = await acceptTask.WaitAsync(Timeout);
        }
        catch (OperationCanceledException)
        {
            // Desired outcome: the stop was observed as cancellation. Nothing to assert further.
            return;
        }

        // If a channel was returned at all, it must be live — not a disposed-pipe corpse.
        await using (accepted)
        {
            Assert.True(
                accepted!.IsConnected,
                "AcceptAsync returned a channel over an already-disposed pipe (IsConnected == false); " +
                "it should have thrown OperationCanceledException instead.");
        }
    }
}
