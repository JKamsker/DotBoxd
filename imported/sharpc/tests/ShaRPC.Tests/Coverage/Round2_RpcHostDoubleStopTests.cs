using ShaRPC.Core;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// DETERMINISTIC red→green regression for DEFECT #1 (round 2): <see cref="RpcHost"/> calls
/// <c>_listener.StopAsync</c> twice when a Stop sequences between <c>StartAsync</c>'s first await
/// and its second lifecycle lock.
///
/// Sequence (the deliberately-deferred "sub-race 2"):
///  1. <c>StartAsync</c> takes the first lock (<c>_cts = cts</c>) and awaits
///     <c>_listener.StartAsync</c>, which returns successfully.
///  2. Before <c>StartAsync</c> reaches its second lock, a full <c>StopAsync()</c> runs to
///     completion: <c>StopCoreAsync</c> calls <c>_listener.StopAsync</c> (call #1) and its
///     <c>finally</c> clears <c>_cts</c> to <c>null</c>.
///  3. <c>StartAsync</c> resumes the second lock. <c>_cts</c> is now <c>null</c>, so
///     <c>!ReferenceEquals(_cts, cts)</c> is true, <c>stopStartedListener = true</c>, and
///     <c>_listener.StopAsync</c> is invoked AGAIN (call #2) even though the transport was already
///     stopped. <see cref="IServerTransport"/> does not document StopAsync as idempotent, so a
///     transport that frees its handle on the first stop faults (or is double-freed) on the second.
///
/// Desired behaviour: the listener is stopped at most once. A single-fire guard on
/// <c>_listener.StopAsync</c> (or awaiting the in-flight <c>_stopTask</c> instead of re-stopping)
/// makes call #2 a no-op, so the count stays 1 and this test turns green.
///
/// Determinism without sleeps/stress: the test uses the inert internal seam
/// <c>RpcHost._onListenerStartedForTest</c> (exposed via InternalsVisibleTo). The seam fires after
/// <c>_listener.StartAsync</c> succeeds and before the second lock; the hook runs the full
/// <c>StopAsync()</c> to completion there, guaranteeing <c>_cts == null</c> before the second lock
/// observes it. This forces the exact interleaving every run.
/// </summary>
public sealed class Round2_RpcHostDoubleStopTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task StopSequencedBeforeStartSecondLock_StopsListenerExactlyOnce()
    {
        var transport = new CountingServerTransport();
        var host = RpcHost.Listen(transport, NewSerializer());

        try
        {
            // Seam: after _listener.StartAsync succeeds and before StartAsync's second lock, run a
            // full StopAsync() to completion so StopCoreAsync clears _cts. When StartAsync resumes,
            // its second lock sees _cts == null and (on the unfixed code) calls StopAsync a 2nd time.
            host._onListenerStartedForTest = () => host.StopAsync();

            // StartAsync's second-lock path stops "early" and throws because the host was stopped
            // before the start completed. We only care that the transport was stopped at most once.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => host.StartAsync().WaitAsync(Timeout));

            Assert.Equal(1, transport.StopCalls);
        }
        finally
        {
            host._onListenerStartedForTest = null;
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// Mock transport whose <see cref="StartAsync"/> completes immediately and whose
    /// <see cref="StopAsync"/> counts every invocation. A real handle-freeing transport would fault
    /// on the second stop; counting makes the double-stop observable without depending on a throw.
    /// </summary>
    private sealed class CountingServerTransport : IServerTransport
    {
        private int _stopCalls;

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("The accept loop should never start in this scenario.");

        public Task StopAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _stopCalls);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;
    }
}
