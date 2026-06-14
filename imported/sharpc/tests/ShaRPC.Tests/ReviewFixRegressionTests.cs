using System.Buffers;
using System.Threading;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

/// <summary>
/// Regression tests for review findings on the peer model: the outbound pending-slot leak (H1)
/// and <see cref="RpcPeerOptions.RequestTimeout"/> validation (M6).
/// </summary>
public sealed class ReviewFixRegressionTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

    // H1: a synchronous serialization failure must release the reserved pending-request slot,
    // otherwise the bounded admission gate leaks a slot per failure and eventually rejects every
    // call with "Maximum pending requests reached" even though nothing is in flight.
    [Fact]
    public async Task OutboundSerializationFailure_DoesNotLeakPendingSlot()
    {
        var serializer = new PoisonSerializer(NewSerializer());
        await using var connection = new BlackHoleConnection();
        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    MaxPendingRequests = 1,
                    RequestTimeout = TimeSpan.FromMilliseconds(250),
                })
            .Start();

        // Each call fails while serializing its argument. With the slot leaked, the SECOND such
        // call would already throw ShaRpcException("Maximum pending requests reached.") instead of
        // the serialization error.
        for (var i = 0; i < 5; i++)
        {
            var failure = await Assert.ThrowsAnyAsync<Exception>(
                () => peer.InvokeAsync<PoisonArgument, int>("Service", "Method", new PoisonArgument()));
            Assert.IsNotType<ShaRpcException>(failure);
        }

        // A subsequent well-formed call still gets a slot and reaches the wire, timing out because
        // the black-hole connection never answers — proving the slots were reclaimed.
        await Assert.ThrowsAsync<ShaRpcTimeoutException>(
            () => peer.InvokeAsync<int>("Service", "Method").WaitAsync(TimeSpan.FromSeconds(2)));
    }

    // M6: RequestTimeout is validated at construction like the other tunables.
    [Fact]
    public void RpcPeerOptions_InvalidRequestTimeout_ThrowsDuringConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RpcPeerOptions { RequestTimeout = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(-5) });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { RequestTimeout = TimeSpan.FromMilliseconds((double)int.MaxValue + 1) });

        // Positive timeouts and the infinite sentinel are accepted.
        _ = new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(1) };
        _ = new RpcPeerOptions { RequestTimeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class PoisonArgument
    {
    }

    /// <summary>Serializer that throws when asked to serialize a <see cref="PoisonArgument"/>.</summary>
    private sealed class PoisonSerializer : ISerializer
    {
        private readonly ISerializer _inner;

        public PoisonSerializer(ISerializer inner) => _inner = inner;

        public void Serialize<T>(IBufferWriter<byte> writer, T value)
        {
            if (typeof(T) == typeof(PoisonArgument))
            {
                throw new InvalidOperationException("Cannot serialize the poison argument.");
            }

            _inner.Serialize(writer, value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data) => _inner.Deserialize<T>(data);

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => _inner.Deserialize(data, type);
    }

    /// <summary>Accepts every send and never produces an inbound frame until disposed.</summary>
    private sealed class BlackHoleConnection : IRpcChannel
    {
        private readonly TaskCompletionSource<bool> _disposedSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "test://black-hole";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) => Task.CompletedTask;

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), _disposedSignal))
            {
                await _disposedSignal.Task.ConfigureAwait(false);
            }

            return Payload.Empty;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            _disposedSignal.TrySetResult(true);
            return default;
        }
    }
}
