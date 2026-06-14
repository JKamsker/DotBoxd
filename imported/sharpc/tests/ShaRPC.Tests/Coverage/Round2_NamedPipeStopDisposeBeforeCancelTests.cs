using ShaRPC.Core.Transport;
using ShaRPC.Transports.NamedPipes;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// DETERMINISTIC red→green regression for DEFECT #5: when <see cref="NamedPipeServerTransport.StopAsync"/>
/// is called while an <c>AcceptAsync</c> is blocked in <c>WaitForConnectionAsync</c> (no client yet),
/// <c>AcceptAsync</c> must surface the stop as <see cref="OperationCanceledException"/> — never as the raw
/// <see cref="ObjectDisposedException"/> that disposing the pending stream produces.
///
/// The defect: <c>StopAsync</c> disposes <c>_pendingStream</c> (inside the lock) BEFORE it cancels the
/// stop source (outside the lock). Disposing the stream wakes the blocked <c>WaitForConnectionAsync</c>
/// with <see cref="ObjectDisposedException"/>, but its catch filter
/// <c>when (ct.IsCancellationRequested)</c> is still false in that window, so the ODE escapes
/// <c>AcceptAsync</c> instead of being converted to cancellation. Callers (e.g. <c>RpcHost</c>'s accept
/// loop) that expect <see cref="OperationCanceledException"/> on stop instead see an
/// <see cref="ObjectDisposedException"/>.
///
/// The natural-timing race window is far smaller than thread-pool/async latency (Cancel() runs only
/// microseconds after the dispose), so this uses an internal <c>_beforeStopCancelForTest</c> seam
/// (exposed via InternalsVisibleTo) to force the exact interleaving: the seam fires inside
/// <c>StopAsync</c> AFTER the pending stream is disposed but BEFORE the stop source is cancelled, and
/// waits there until the blocked <c>AcceptAsync</c> has fully completed. On the UNFIXED code that
/// completion is an <see cref="ObjectDisposedException"/> (RED). The fix moves <c>stopCts.Cancel()</c>
/// inside the lock so the token is already cancelled when <c>WaitForConnectionAsync</c> wakes, turning
/// the wake into <see cref="OperationCanceledException"/> (GREEN).
/// </summary>
public sealed class Round2_NamedPipeStopDisposeBeforeCancelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static string CreatePipeName() => "sharpc-round2-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task AcceptAsync_WhenStopDisposesPendingStreamBeforeCancel_ThrowsOperationCanceledNotObjectDisposed()
    {
        await using var server = new NamedPipeServerTransport(CreatePipeName());
        await server.StartAsync();

        // No client ever connects, so this accept parks inside WaitForConnectionAsync until the stream
        // it owns is disposed by StopAsync below.
        var acceptTask = server.AcceptAsync();

        // Seam: runs inside StopAsync after _pendingStream.Dispose() and before stopCts.Cancel(). Park
        // StopAsync here until the blocked AcceptAsync has fully unwound from the now-disposed stream.
        // That reproduces the production dispose-before-cancel window deterministically: AcceptAsync
        // observes the disposed stream while the stop token is still un-cancelled.
        server._beforeStopCancelForTest = async () =>
        {
            try
            {
                await acceptTask.WaitAsync(Timeout).ConfigureAwait(false);
            }
            catch
            {
                // The accept is expected to fault here; the test inspects that fault below. Swallow so
                // StopAsync still proceeds to cancel and complete normally.
            }
        };

        await server.StopAsync().WaitAsync(Timeout);

        // Correct/desired behavior: stopping a pending accept surfaces as cancellation, regardless of the
        // dispose-before-cancel ordering. The unfixed code lets the raw ObjectDisposedException escape
        // (RED); the fix cancels the token before the disposed stream is observed, yielding OCE (GREEN).
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => acceptTask);

        Assert.IsNotType<ObjectDisposedException>(ex);
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }
}
