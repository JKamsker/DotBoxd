using System.Buffers;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

/// <summary>
/// Regression tests for the peer-model review iteration that hardened the outbound invoke
/// boundary and the close/dispose contract.
/// </summary>
public sealed class RpcPeerInvokeAndCloseReviewTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

    [Theory]
    [InlineData(null, "Method")]
    [InlineData("", "Method")]
    [InlineData("Service", null)]
    [InlineData("Service", "")]
    public async Task InvokeAsync_Throws_OnNullOrEmptyServiceOrMethod(string? service, string? method)
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        await using var peer = RpcPeer.Over(
            serverConnection,
            NewSerializer(),
            new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) });

        // The four public shapes route through the two private SendRequestAsync overloads.
        await Assert.ThrowsAsync<ArgumentException>(() => peer.InvokeAsync<int>(service!, method!));
        await Assert.ThrowsAsync<ArgumentException>(() => peer.InvokeAsync<int, int>(service!, method!, 1));
        await Assert.ThrowsAsync<ArgumentException>(() => peer.InvokeAsync(service!, method!));
        await Assert.ThrowsAsync<ArgumentException>(
            () => peer.InvokeOnInstanceAsync<int>(service!, "instance", method!));
    }

    [Fact]
    public async Task InvokeAsync_ValidatesBeforeStartingTheReadLoop()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        await using var peer = RpcPeer.Over(
            serverConnection,
            NewSerializer(),
            new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) });

        // An invalid call must not implicitly start the peer (validation precedes EnsureStarted).
        await Assert.ThrowsAsync<ArgumentException>(() => peer.InvokeAsync<int>("", "Method"));

        // Providing a service is only allowed before start, so it succeeding proves the peer is unstarted.
        var ex = Record.Exception(() => peer.Provide((IServiceDispatcher)new NoopDispatcher()));
        Assert.Null(ex);
    }

    [Fact]
    public async Task CloseAsync_FailsFast_WhenTokenAlreadyCancelled()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        var peer = RpcPeer
            .Over(serverConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => peer.CloseAsync(cts.Token));

        // Failing fast must not have torn anything down; an explicit dispose still completes cleanly.
        await peer.DisposeAsync();
        Assert.False(peer.IsConnected);
    }

    [Fact]
    public async Task CloseAsync_CompletesDisposal_WhenTokenCancelledMidDispose()
    {
        var channel = new GatedDisposeChannel();
        var peer = RpcPeer
            .Over(channel, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        using var cts = new CancellationTokenSource();
        var close = peer.CloseAsync(cts.Token);

        // Disposal has begun and is parked inside the channel's DisposeAsync.
        await channel.DisposeEntered.WaitAsync(TimeSpan.FromSeconds(1));

        // Cancelling now must NOT abandon the in-progress dispose with an OperationCanceledException.
        cts.Cancel();
        channel.ReleaseDispose();

        // Completes cleanly (no throw); under the old behavior this awaited task faulted with OCE.
        await close.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(peer.IsConnected);
    }

    private sealed class NoopDispatcher : IServiceDispatcher
    {
        public string ServiceName => "Noop";

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// A channel whose <see cref="DisposeAsync"/> parks until released, so a test can observe that
    /// <see cref="RpcPeer.CloseAsync"/> keeps running cleanup even after its token is cancelled.
    /// </summary>
    private sealed class GatedDisposeChannel : IRpcChannel
    {
        private readonly TaskCompletionSource<bool> _disposeEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseDispose =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        public Task DisposeEntered => _disposeEntered.Task;

        public void ReleaseDispose() => _releaseDispose.TrySetResult(true);

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "gated://remote";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) => Task.CompletedTask;

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            // Park the read loop until the peer's token cancels on shutdown.
            var parked = new TaskCompletionSource<Payload>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(static state => ((TaskCompletionSource<Payload>)state!).TrySetResult(Payload.Empty), parked))
            {
                return await parked.Task.ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _disposeEntered.TrySetResult(true);
            await _releaseDispose.Task.ConfigureAwait(false);
        }
    }
}
