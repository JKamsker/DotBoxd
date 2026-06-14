using ShaRPC.Core;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// RED regression for the <c>_cts</c> state leak in <see cref="RpcHost"/>.<c>StartAsync</c>.
///
/// When the caller-supplied <see cref="CancellationToken"/> is already cancelled but the
/// transport's <c>StartAsync</c> still succeeds, <c>StartAsync</c> enters the
/// <c>_stopTask is not null || cts.IsCancellationRequested</c> branch (RpcHost.cs ~line 157):
/// it stops the just-started listener and throws
/// <c>InvalidOperationException("Host start was stopped before it completed.")</c>. Unlike the
/// disposed branch and the catch block, that branch does NOT null <c>_cts</c> nor dispose the
/// linked CTS. The cancelled, undisposed <c>_cts</c> therefore lingers, so any subsequent
/// <c>StartAsync</c> hits the <c>_cts is not null</c> guard and wrongly throws
/// "Host is already running." even though no accept loop ever ran.
///
/// Desired behaviour: a start that fails because the caller token was already cancelled must
/// leave the host in a clean, restartable state — a later <c>StartAsync</c> with a live token
/// must succeed (and must NOT throw "Host is already running."). The fix (null <c>_cts</c> and
/// dispose the linked CTS in that branch, mirroring the catch block) makes this test green.
///
/// Fully deterministic and single-threaded: the transport's <c>StartAsync</c> simply awaits one
/// <see cref="Task.Yield"/> and returns (it never throws and ignores its token), and its
/// <c>AcceptAsync</c> parks on its token so the second start's accept loop stays quietly alive.
/// </summary>
public sealed class Round2_RpcHostStartCancelStateLeakTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task StartAsync_WithPreCancelledToken_DoesNotLeaveHostMarkedAlreadyRunning()
    {
        var transport = new YieldingStartServerTransport();
        await using var host = RpcHost.Listen(transport, NewSerializer());

        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();

        // First start: the transport start succeeds but the caller token is already cancelled, so
        // StartAsync stops the listener and throws "Host start was stopped before it completed.".
        var firstFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync(preCancelled.Token).WaitAsync(Timeout));
        Assert.Contains("stopped before it completed", firstFailure.Message);

        // The first (failed) start must have left the host restartable. On the unfixed code _cts is
        // still the cancelled, undisposed CTS, so this second start throws "Host is already running.".
        // That is the bug under test — assert the second start SUCCEEDS instead.
        var secondStartFailure = await Record.ExceptionAsync(
            () => host.StartAsync().WaitAsync(Timeout));

        Assert.Null(secondStartFailure);

        // And the transport must actually have been (re)started, proving a real restart rather than
        // a silently swallowed no-op.
        Assert.Equal(2, transport.StartCalls);
    }

    /// <summary>
    /// Transport whose <c>StartAsync</c> always succeeds (after one <see cref="Task.Yield"/>) and
    /// ignores its token, and whose <c>AcceptAsync</c> parks on its token so a started accept loop
    /// stays alive without faulting until shutdown.
    /// </summary>
    private sealed class YieldingStartServerTransport : IServerTransport
    {
        private int _startCalls;

        public int StartCalls => Volatile.Read(ref _startCalls);

        public async Task StartAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _startCalls);

            // Complete asynchronously but successfully, ignoring the (possibly cancelled) token so
            // StartAsync reaches its second lock instead of the catch block.
            await Task.Yield();
        }

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            // Park until the host cancels us during shutdown; never hand back a connection.
            var parked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), parked))
            {
                await parked.Task.ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            throw new OperationCanceledException();
        }

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;
    }
}
