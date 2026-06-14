using ShaRPC.Core;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round 4 regression for <see cref="RpcDiagnostics.Report"/>. Report built its trace message — including
/// the virtual <c>error.Message</c>, and in the subscriber-failure path the subscriber exception's
/// <c>ToString()</c> — as a string-interpolation ARGUMENT, evaluated before <c>SafeTrace</c>'s try/catch was
/// entered. A custom exception whose <c>Message</c> getter or <c>ToString()</c> throws therefore escaped
/// Report, breaking its documented "isolated from each other and from RPC internals" guarantee (the Round 2
/// fix only wrapped <c>Trace.TraceError</c>, not the message construction). The message must be built inside
/// the guard.
///
/// <para><c>Report</c> is internal, reachable via <c>InternalsVisibleTo("ShaRPC.Tests")</c>.</para>
/// </summary>
public sealed class Round4_DiagnosticsMessageConstructionTests
{
    // RpcDiagnostics.Error is a process-global static event; serialize this class's two methods. The
    // subscriber below is sentinel-gated, so the suite's other diagnostics tests stay unaffected in parallel.
    private static readonly SemaphoreSlim s_gate = new(1, 1);

    [Fact]
    public async Task Report_WhenErrorMessageGetterThrows_DoesNotEscape()
    {
        await s_gate.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            // The error's virtual Message getter throws while Report formats its first trace line. With the
            // bug that throw escapes Report; the fix builds the message inside SafeTrace's guard.
            var escaped = Record.Exception(
                () => RpcDiagnostics.Report("round4-msg-getter", new ThrowingMessageException()));

            Assert.Null(escaped);
        }
        finally
        {
            s_gate.Release();
        }
    }

    [Fact]
    public async Task Report_WhenFaultingSubscriberExceptionToStringThrows_DoesNotEscape()
    {
        await s_gate.WaitAsync(TimeSpan.FromSeconds(30));

        var sentinel = Guid.NewGuid().ToString("N");
        var sentinelError = new InvalidOperationException("round4 subscriber sentinel " + sentinel);

        // Throw ONLY on our own report so we never disturb (or react to) a parallel test's report. The
        // thrown exception's ToString() itself throws, which is the payload Report's catch tries to log.
        void FaultingSubscriber(object? sender, RpcDiagnosticErrorEventArgs args)
        {
            if (ReferenceEquals(args.Error, sentinelError))
            {
                throw new ThrowingToStringException();
            }
        }

        RpcDiagnostics.Error += FaultingSubscriber;
        try
        {
            // Report catches the subscriber's throw, then interpolates {subscriberError} when logging the
            // failure. Unguarded, that ToString() re-throws out of Report; the fix formats inside SafeTrace.
            var escaped = Record.Exception(
                () => RpcDiagnostics.Report("round4-subscriber-tostring-" + sentinel, sentinelError));

            Assert.Null(escaped);
        }
        finally
        {
            RpcDiagnostics.Error -= FaultingSubscriber;
            s_gate.Release();
        }
    }

    private sealed class ThrowingMessageException : Exception
    {
        public override string Message => throw new InvalidOperationException("message getter throws");
    }

    private sealed class ThrowingToStringException : Exception
    {
        public override string Message => "benign";

        public override string ToString() => throw new InvalidOperationException("ToString throws");
    }
}
