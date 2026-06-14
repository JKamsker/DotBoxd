using ShaRPC.Core;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// RED regression for the recovery-path exception masking (and linked-CTS leak) in
/// <see cref="RpcHost"/>.<c>StartAsync</c>.
///
/// When the caller-supplied <see cref="CancellationToken"/> is already cancelled but the
/// transport's <c>StartAsync</c> still succeeds, <c>StartAsync</c> enters the
/// <c>cts.IsCancellationRequested</c> recovery branch (RpcHost.cs ~line 179): it sets
/// <c>startFailure = InvalidOperationException("Host start was stopped before it completed.")</c>,
/// arms <c>disposeCts = true</c>, then runs the best-effort recovery
/// <c>await _listener.StopAsync(CancellationToken.None)</c> inside a <c>try { ... } finally { _starting = false; }</c>.
/// Only AFTER that <c>try</c> block does it run <c>if (disposeCts) cts.Dispose();</c> and
/// <c>throw startFailure;</c>.
///
/// The defect: if the recovery <c>_listener.StopAsync</c> THROWS, the exception unwinds through
/// the <c>finally</c> (which only resets <c>_starting</c>), skipping BOTH <c>cts.Dispose()</c>
/// (leaking the linked CTS) AND <c>throw startFailure</c>. The caller therefore observes the
/// raw <c>StopAsync</c> exception ("stop fault") instead of the intended
/// "Host start was stopped before it completed.".
///
/// Desired behaviour: the best-effort recovery stop must neither mask the real start outcome nor
/// leak the linked CTS. The thrown exception's message must report that the start was stopped
/// before it completed, NOT the recovery-stop fault. The suggested fix (wrap the recovery
/// <c>StopAsync</c> in try/catch and move <c>cts.Dispose()</c> into a finally) makes this green.
///
/// Fully deterministic and single-threaded: the pre-cancelled caller token guarantees the
/// <c>cts.IsCancellationRequested</c> branch; the transport's <c>StartAsync</c> simply awaits one
/// <see cref="Task.Yield"/> and returns (it never throws and ignores its token); its
/// <c>StopAsync</c> always throws synchronously after a yield.
/// </summary>
public sealed class Round3_StartRecoveryStopThrowsCtsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task StartAsync_PreCancelled_RecoveryStopThrows_StillReportsStartStopped()
    {
        var transport = new StopThrowingServerTransport();
        await using var host = RpcHost.Listen(transport, NewSerializer());

        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();

        // The transport start succeeds but the caller token is already cancelled, so StartAsync
        // enters the recovery branch and best-effort-stops the just-started listener. That stop
        // throws "stop fault". On the unfixed code the recovery exception escapes and masks the
        // intended start outcome, so this throws an InvalidOperationException whose message is
        // "stop fault" instead of "Host start was stopped before it completed." -> RED.
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync(preCancelled.Token).WaitAsync(Timeout));

        // The recovery stop did run (proving we are exercising the path under test)...
        Assert.True(transport.StopCalls > 0);

        // ...but its fault must NOT be what the caller sees. The start outcome must win.
        Assert.DoesNotContain("stop fault", failure.Message);
        Assert.Contains("stopped before it completed", failure.Message);
    }

    /// <summary>
    /// Transport whose <c>StartAsync</c> always succeeds (after one <see cref="Task.Yield"/>) and
    /// ignores its token so <c>StartAsync</c> reaches its second lock, and whose <c>StopAsync</c>
    /// always throws <see cref="InvalidOperationException"/> ("stop fault").
    /// </summary>
    private sealed class StopThrowingServerTransport : IServerTransport
    {
        private int _stopCalls;

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public async Task StartAsync(CancellationToken ct = default)
        {
            // Complete asynchronously but successfully, ignoring the (already cancelled) token so
            // StartAsync reaches its second lock instead of the catch block.
            await Task.Yield();
        }

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            // The recovery branch never launches the accept loop; park defensively if ever called.
            var parked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), parked))
            {
                await parked.Task.ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            throw new OperationCanceledException();
        }

        public async Task StopAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _stopCalls);
            await Task.Yield();
            throw new InvalidOperationException("stop fault");
        }

        public ValueTask DisposeAsync() => default;
    }
}
