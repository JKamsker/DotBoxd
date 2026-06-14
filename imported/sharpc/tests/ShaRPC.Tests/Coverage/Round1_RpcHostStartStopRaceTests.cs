using ShaRPC.Core;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// RED regression for the StartAsync/StopAsync concurrency defect in <see cref="RpcHost"/>.
///
/// When <c>StopAsync</c> runs while <c>StartAsync</c> is still blocked inside
/// <c>_listener.StartAsync</c>, <c>StopAsync</c> installs a running <c>StopCoreAsync</c>
/// (which has already reached <c>_listener.StopAsync</c>). The cancellation then makes
/// <c>_listener.StartAsync</c> throw, and <c>StartAsync</c>'s catch block clears
/// <c>_cts</c>/<c>_stopTask</c> because <c>ReferenceEquals(_cts, cts)</c> is true — hiding the
/// still-running <c>StopCoreAsync</c> from any future Stop/Dispose. A subsequent
/// <c>DisposeAsync</c> then sees <c>_cts == null</c>, returns immediately from <c>StopAsync</c>,
/// and proceeds to <c>_listener.DisposeAsync()</c> while the orphaned <c>StopCoreAsync</c> is
/// still inside <c>_listener.StopAsync</c> — a dispose-before-stop-completes ordering violation
/// on the same transport.
///
/// The desired behaviour: DisposeAsync must wait for the in-flight stop to finish before
/// disposing the transport, so <c>_listener.StopAsync</c> and <c>_listener.DisposeAsync</c> never
/// overlap. The fix (leave <c>_stopTask</c> intact in the catch when a stop is in flight) makes
/// this test green.
///
/// Determinism without deadlock: the mock transport's <c>StopAsync</c> parks on a gate that the
/// TEST releases (never <c>DisposeAsync</c>). The test invokes <c>DisposeAsync</c> while the stop
/// is still parked and only releases the stop afterwards. On the unfixed code
/// <c>host.DisposeAsync()</c> completes synchronously (its <c>StopAsync()</c> returns
/// <c>Task.CompletedTask</c>), so <c>_listener.DisposeAsync</c> runs while the stop is in flight
/// (overlap recorded). On the fixed code <c>host.DisposeAsync()</c> suspends awaiting the in-flight
/// stop and only disposes the transport after the test releases it — no overlap.
/// </summary>
public sealed class Round1_RpcHostStartStopRaceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task StopDuringStart_OrphanedStopCore_DoesNotLetDisposeOverlapTransportStop()
    {
        var transport = new GatedStartStopServerTransport();
        var host = RpcHost.Listen(transport, NewSerializer());

        // 1) StartAsync enters _listener.StartAsync and blocks (it only completes on cancellation).
        var startTask = host.StartAsync();
        await transport.StartEntered.WaitAsync(Timeout);

        // 2) StopAsync installs StopCoreAsync, which cancels cts (unblocking StartAsync into its
        //    throw path) and then parks inside _listener.StopAsync awaiting the test's release.
        var stopTask = host.StopAsync();

        // 3) The cancelled StartAsync surfaces an OperationCanceledException via its catch block,
        //    which on the unfixed code nulls _cts/_stopTask and orphans the running StopCoreAsync.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => startTask.WaitAsync(Timeout));

        // 4) Ensure the orphaned StopCoreAsync has genuinely entered _listener.StopAsync, so an
        //    overlap with DisposeAsync would be observable.
        await transport.StopEntered.WaitAsync(Timeout);

        // 5) Begin DisposeAsync while the stop is still parked. Do NOT await it yet.
        //    - Unfixed: host.DisposeAsync()'s StopAsync() sees _cts == null and returns synchronously,
        //      so _listener.DisposeAsync runs NOW (while the stop is in flight) -> overlap recorded.
        //    - Fixed: host.DisposeAsync()'s StopAsync() returns the in-flight _stopTask and suspends,
        //      so _listener.DisposeAsync has NOT run yet.
        var disposeTask = host.DisposeAsync();

        // 6) Release the parked stop. On the fixed path this lets the in-flight StopCoreAsync (and the
        //    DisposeAsync awaiting it) complete; on the unfixed path it just lets the orphan finish.
        transport.ReleaseStop();

        await disposeTask.AsTask().WaitAsync(Timeout);
        await stopTask.WaitAsync(Timeout);

        // The transport's StopAsync and DisposeAsync must never have overlapped.
        Assert.False(
            transport.DisposeOverlappedStop,
            "DisposeAsync must wait for the in-flight StopAsync to finish before disposing the transport");
    }

    /// <summary>
    /// Mock transport that:
    ///  - blocks inside StartAsync until its token is cancelled (then throws OCE),
    ///  - blocks inside StopAsync until the test calls <see cref="ReleaseStop"/>,
    ///  - records whether DisposeAsync was entered while a StopAsync was still in flight.
    /// </summary>
    private sealed class GatedStartStopServerTransport : IServerTransport
    {
        private readonly TaskCompletionSource<bool> _startEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _stopEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _allowStopComplete =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _stopInFlight;
        private int _disposeOverlappedStop;

        public Task StartEntered => _startEntered.Task;

        public Task StopEntered => _stopEntered.Task;

        public bool DisposeOverlappedStop => Volatile.Read(ref _disposeOverlappedStop) != 0;

        /// <summary>Releases a parked <see cref="StopAsync"/>. Called only by the test.</summary>
        public void ReleaseStop() => _allowStopComplete.TrySetResult(true);

        public async Task StartAsync(CancellationToken ct = default)
        {
            _startEntered.TrySetResult(true);

            // Block until cancelled; cancellation (from StopCoreAsync's cts.Cancel) surfaces as OCE,
            // driving StartAsync into its catch block.
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancelled))
            {
                await cancelled.Task.ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            throw new OperationCanceledException();
        }

        public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("The accept loop should never start in this scenario.");

        public async Task StopAsync(CancellationToken ct = default)
        {
            Interlocked.Exchange(ref _stopInFlight, 1);
            _stopEntered.TrySetResult(true);
            try
            {
                // Park here until the test releases the stop, so an overlapping DisposeAsync on the
                // unfixed code is observable. The test (never DisposeAsync) releases this gate.
                await _allowStopComplete.Task.ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _stopInFlight, 0);
            }
        }

        public ValueTask DisposeAsync()
        {
            // Record whether a StopAsync is still running at the moment dispose begins.
            if (Volatile.Read(ref _stopInFlight) != 0)
            {
                Interlocked.Exchange(ref _disposeOverlappedStop, 1);
            }

            return default;
        }
    }
}
