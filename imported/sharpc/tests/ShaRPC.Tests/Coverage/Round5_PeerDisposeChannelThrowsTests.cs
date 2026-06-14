using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round 5 regression for <see cref="RpcPeer"/> teardown. <c>DisposeCoreAsync</c> awaited
/// <c>_channel.DisposeAsync()</c> with no try/catch as its first step. A user-supplied
/// <see cref="IRpcChannel"/> whose <c>DisposeAsync</c> throws therefore aborted the rest of teardown —
/// <c>FailPending</c>, inbound stop, and semaphore/CTS disposal were all skipped, leaking handles and
/// leaving pending outbound calls hung forever. (The read-loop's own <c>FailPending</c> does not cover
/// this: dispose cancels the loop's token, so its <c>finally</c> skips the remote-close path.) Channel
/// disposal must be best-effort so teardown always completes.
/// </summary>
public sealed class Round5_PeerDisposeChannelThrowsTests
{
    [Fact]
    public async Task DisposeAsync_WhenChannelDisposeThrows_StillFailsPendingOutboundRequests()
    {
        var channel = new ThrowingDisposeChannel();
        var peer = RpcPeer.Over(channel, new MessagePackRpcSerializer()).Start();

        var invoke = peer.InvokeAsync<int, string>("Svc", "Op", request: 1);

        // Ensure the request is on the wire (hence registered as pending) before teardown.
        await channel.RequestSent.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The channel's DisposeAsync throws. On the bug this aborts DisposeCoreAsync before FailPending,
        // so the pending call hangs; teardown must continue and fail it. DisposeAsync may surface the
        // channel fault on the buggy path, so tolerate it here.
        await Record.ExceptionAsync(async () => await peer.DisposeAsync());

        // The pending call must be failed (promptly) with a connection exception. On the bug it hangs and
        // WaitAsync times out instead.
        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => invoke.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.IsType<ShaRpcConnectionException>(error);
    }

    private sealed class ThrowingDisposeChannel : IRpcChannel
    {
        private readonly TaskCompletionSource<bool> _parked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        public TaskCompletionSource<bool> RequestSent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "throwing-dispose://remote";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            RequestSent.TrySetResult(true);
            return Task.CompletedTask;
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            using (ct.Register(static s => ((TaskCompletionSource<bool>)s!).TrySetResult(true), _parked))
            {
                await _parked.Task.ConfigureAwait(false);
            }

            return Payload.Empty;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            _parked.TrySetResult(true); // unblock the read loop before failing
            throw new InvalidOperationException("channel dispose failed");
        }
    }
}
