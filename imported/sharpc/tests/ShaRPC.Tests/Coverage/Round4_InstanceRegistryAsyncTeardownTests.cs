using System.Collections.Concurrent;
using ShaRPC.Core.Server;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round 4 regression for <see cref="InstanceRegistry"/>'s connection-teardown drain. It disposed
/// sub-service instances via <c>DisposeAsync().AsTask().GetAwaiter().GetResult()</c> — sync-over-async that
/// blocks the calling thread for the full duration of a user <see cref="IAsyncDisposable.DisposeAsync"/>.
/// The in-code comment argued this was safe because ShaRPC runs context-free, but it only covers
/// SynchronizationContext deadlocks <em>of ShaRPC's own awaits</em>: a user disposer that suspends and
/// captures a context (the default when it omits <c>ConfigureAwait(false)</c>) deadlocks the blocked thread,
/// and under concurrent teardown the blocking waits also starve the thread pool. The drain must instead
/// await disposal.
///
/// <para>
/// Proven deterministically: the drain runs on a single-threaded message pump while a registered disposer
/// suspends on a context-capturing await. Sync-over-async blocks the pump thread so the captured
/// continuation can never run (deadlock → the run times out); a true async drain yields and completes.
/// <c>ReleaseAllAsync</c> is internal, reachable here via <c>InternalsVisibleTo("ShaRPC.Tests")</c>.
/// </para>
/// </summary>
public sealed class Round4_InstanceRegistryAsyncTeardownTests
{
    [Fact]
    public void ReleaseAllAsync_AwaitsDisposal_WithoutSyncBlocking_WhenDisposeAsyncCapturesContext()
    {
        var completed = PumpRunner.RunWithTimeout(async () =>
        {
            var registry = new InstanceRegistry();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            registry.Register("svc", new ContextCapturingAsyncDisposable(gate.Task));

            // On the buggy sync-over-async path this call never returns: GetResult() blocks the pump thread
            // while the captured DisposeAsync continuation is queued to that same (blocked) thread.
            var release = registry.ReleaseAllAsync();
            gate.SetResult();
            await release;
        }, TimeSpan.FromSeconds(5));

        Assert.True(
            completed,
            "teardown deadlocked: sub-service disposal sync-blocked the calling thread instead of awaiting DisposeAsync");
    }

    private sealed class ContextCapturingAsyncDisposable : IAsyncDisposable
    {
        private readonly Task _gate;

        public ContextCapturingAsyncDisposable(Task gate) => _gate = gate;

        public async ValueTask DisposeAsync()
        {
            // Deliberately NO ConfigureAwait(false): captures the current SynchronizationContext, exactly
            // the case the registry comment claimed could never deadlock the blocking GetResult().
            await _gate;
        }
    }

    /// <summary>
    /// Runs an async delegate to completion on a dedicated single-threaded message pump and reports whether
    /// it finished within the timeout. A sync-over-async wait that blocks the pump thread deadlocks (its
    /// captured continuation can never be pumped), so the timeout elapses and the run reports false — the
    /// deterministic RED signal. No global thread-pool tampering, so it stays isolated from the suite.
    /// </summary>
    private static class PumpRunner
    {
        public static bool RunWithTimeout(Func<Task> asyncMethod, TimeSpan timeout)
        {
            using var done = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                var context = new PumpSyncContext();
                var previous = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(context);
                try
                {
                    var root = asyncMethod();
                    root.ContinueWith(_ => context.Complete(), TaskScheduler.Default);
                    context.PumpUntilComplete();
                    if (root.IsCompletedSuccessfully)
                    {
                        done.Set();
                    }
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
            })
            {
                IsBackground = true,
                Name = "round4-teardown-pump",
            };

            thread.Start();
            return done.Wait(timeout);
        }
    }

    private sealed class PumpSyncContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                _queue.Add((d, state));
            }
            catch (InvalidOperationException)
            {
                // Pump already completed; a late continuation has nowhere to run, which is fine on the
                // success path (nothing more is awaited) and irrelevant on the deadlock path.
            }
        }

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public void Complete() => _queue.CompleteAdding();

        public void PumpUntilComplete()
        {
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }
    }
}
