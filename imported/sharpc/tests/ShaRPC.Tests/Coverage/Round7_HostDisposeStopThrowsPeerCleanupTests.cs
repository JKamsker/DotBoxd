using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round 7 regression for <see cref="RpcHost.DisposeAsync"/>. When the transport's <c>StopAsync</c> throws,
/// <c>StopCoreAsync</c> rethrows before reaching <c>CloseAllAsync</c>/<c>AwaitCleanupAsync</c> (they are
/// sequenced after the listener stop), and <c>DisposeAsync</c> only disposed the listener before
/// rethrowing. Because <c>_disposed</c> is already 1 (no retry possible), every accepted peer was stranded
/// with a live read loop and open channel. Dispose must still close accepted peers even when stop throws.
/// </summary>
public sealed class Round7_HostDisposeStopThrowsPeerCleanupTests
{
    private static readonly TimeSpan Timeout5s = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task DisposeAsync_WhenTransportStopThrows_StillClosesAcceptedPeers()
    {
        var channel = new ParkedChannel();
        var transport = new StopThrowingServerTransport(channel);
        var connected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        var host = RpcHost.Listen(transport, new MessagePackRpcSerializer());
        host.PeerConnected += (_, e) => connected.TrySetResult(e.Peer);
        await host.StartAsync().WaitAsync(Timeout5s);

        var peer = await connected.Task.WaitAsync(Timeout5s);
        Assert.True(peer.IsConnected);

        // DisposeAsync still surfaces the transport stop fault, but the accepted peer must be closed
        // regardless — Dispose is the last chance (the _disposed guard blocks any retry).
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.DisposeAsync());

        Assert.False(peer.IsConnected);
    }

    /// <summary>Hands out one channel, then parks further accepts; its StopAsync always throws.</summary>
    private sealed class StopThrowingServerTransport : IServerTransport
    {
        private readonly IRpcChannel _channel;
        private readonly TaskCompletionSource<bool> _parked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _accepted;

        public StopThrowingServerTransport(IRpcChannel channel) => _channel = channel;

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _accepted, 1) == 0)
            {
                return _channel;
            }

            using (ct.Register(static s => ((TaskCompletionSource<bool>)s!).TrySetResult(true), _parked))
            {
                await _parked.Task.ConfigureAwait(false);
            }

            throw new OperationCanceledException(ct);
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            _parked.TrySetResult(true);
            throw new InvalidOperationException("stop boom");
        }

        public ValueTask DisposeAsync() => default;
    }

    /// <summary>A channel whose read loop parks until disposed; IsConnected flips to false on dispose.</summary>
    private sealed class ParkedChannel : IRpcChannel
    {
        private readonly TaskCompletionSource<bool> _parked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "parked://remote";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) => Task.CompletedTask;

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
            _parked.TrySetResult(true);
            return default;
        }
    }
}
