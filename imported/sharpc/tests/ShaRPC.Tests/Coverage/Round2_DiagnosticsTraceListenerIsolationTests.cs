using System.Diagnostics;
using ShaRPC.Core;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Regression coverage for <see cref="RpcDiagnostics.Report"/>'s subscriber-isolation contract when
/// a custom <see cref="TraceListener"/> faults.
///
/// <para>
/// Defect #13: inside <c>Report</c>'s catch block, the fallback <c>Trace.TraceError(...)</c> that logs
/// a failing diagnostic subscriber is itself unprotected. If a registered <see cref="TraceListener"/>
/// throws while emitting that fallback message, the exception escapes <c>Report</c> (and therefore the
/// caller, e.g. <c>RpcEventHandlerInvoker.Raise</c>), breaking the documented isolation guarantee.
/// </para>
///
/// <para>
/// <c>Report</c> is internal but reachable from this assembly via
/// <c>InternalsVisibleTo("ShaRPC.Tests")</c> on ShaRPC.Core. The test exercises it directly.
/// </para>
///
/// <para>
/// Test hygiene: <see cref="RpcDiagnostics.Error"/> is a process-wide static event and
/// <see cref="Trace.Listeners"/> is global mutable state, so this test serializes on a shared gate,
/// uses a unique sentinel operation/exception so it never reacts to another test's report, and
/// restores both the listener collection and the event subscription in <c>finally</c> blocks.
/// </para>
/// </summary>
public sealed class Round2_DiagnosticsTraceListenerIsolationTests
{
    // RpcDiagnostics.Error and Trace.Listeners are process-global. Serialize against the other
    // diagnostics-event tests so handlers/listeners from one test never observe another's report.
    private static readonly SemaphoreSlim s_diagnosticsGate = new(1, 1);

    [Fact]
    public async Task Report_WhenTraceListenerThrowsLoggingSubscriberFailure_DoesNotEscape()
    {
        await s_diagnosticsGate.WaitAsync(TimeSpan.FromSeconds(30));

        // A unique marker so neither our handler nor our listener reacts to any other test's report.
        var sentinel = Guid.NewGuid().ToString("N");
        var sentinelOperation = "Round2-trace-isolation-" + sentinel;
        var sentinelError = new InvalidOperationException("round2 sentinel fault " + sentinel);

        // Forces Report into its catch block. The thrown message carries the sentinel so the listener
        // below fires ONLY on Report's fallback "diagnostic handler failed" log line (the cited defect
        // path), never on Report's first/unrelated Trace.TraceError nor on any other test's report.
        void FaultingSubscriber(object? sender, RpcDiagnosticErrorEventArgs args)
        {
            if (ReferenceEquals(args.Error, sentinelError))
            {
                throw new Exception("hostile subscriber " + sentinel);
            }
        }

        // Snapshot the listener collection so we can restore it exactly, regardless of outcome.
        var originalListeners = new TraceListener[Trace.Listeners.Count];
        Trace.Listeners.CopyTo(originalListeners, 0);

        // The listener throws only when it observes the fallback "diagnostic handler failed" message
        // that carries our sentinel — i.e. exactly the unprotected Trace.TraceError in Report's catch.
        var faultingListener = new FaultingTraceListener(sentinel);

        RpcDiagnostics.Error += FaultingSubscriber;
        Trace.Listeners.Add(faultingListener);
        try
        {
            // The faulting subscriber drives Report into its catch, whose fallback Trace.TraceError
            // makes the listener throw. With the bug, that listener exception escapes Report.
            var escaped = Record.Exception(() => RpcDiagnostics.Report(sentinelOperation, sentinelError));

            Assert.Null(escaped);

            // Sanity: the listener really was reached on the fallback-logging path (so the test would
            // be vacuously green only if Report stopped calling Trace.TraceError on subscriber failure).
            Assert.True(faultingListener.WasInvoked);
        }
        finally
        {
            RpcDiagnostics.Error -= FaultingSubscriber;

            Trace.Listeners.Clear();
            Trace.Listeners.AddRange(originalListeners);

            s_diagnosticsGate.Release();
        }
    }

    /// <summary>
    /// A <see cref="TraceListener"/> that throws ONLY when it observes Report's fallback
    /// "diagnostic handler failed" message carrying our sentinel marker — that is, exactly the
    /// unprotected <c>Trace.TraceError</c> inside Report's catch block. It deliberately ignores
    /// Report's first (operation-level) trace line and any traffic from the rest of the suite, so the
    /// test stays inert except on the precise defect path and the fix on that line turns it green.
    /// It records whether it was reached so the test can assert the fallback-logging path ran.
    /// </summary>
    private sealed class FaultingTraceListener : TraceListener
    {
        // Report's catch logs: $"ShaRPC diagnostic handler failed: {subscriberError}".
        private const string FallbackLogPrefix = "ShaRPC diagnostic handler failed";

        private readonly string _sentinel;

        public FaultingTraceListener(string sentinel) => _sentinel = sentinel;

        public bool WasInvoked { get; private set; }

        public override void Write(string? message) => Fail(message);

        public override void WriteLine(string? message) => Fail(message);

        public override void TraceEvent(
            TraceEventCache? eventCache,
            string source,
            TraceEventType eventType,
            int id,
            string? message)
        {
            MaybeFault(message);
        }

        public override void TraceEvent(
            TraceEventCache? eventCache,
            string source,
            TraceEventType eventType,
            int id,
            string? format,
            params object?[]? args)
        {
            var rendered = args is null || format is null ? format : string.Format(format, args);
            MaybeFault(rendered);
        }

        private void MaybeFault(string? message)
        {
            // Fire only on Report's catch-block fallback log that references our sentinel. This skips
            // Report's first Trace.TraceError (operation line) so the test pins the cited catch path.
            if (message is null
                || !message.Contains(FallbackLogPrefix, StringComparison.Ordinal)
                || !message.Contains(_sentinel, StringComparison.Ordinal))
            {
                return;
            }

            WasInvoked = true;
            throw new InvalidOperationException("trace listener is hostile");
        }
    }
}
