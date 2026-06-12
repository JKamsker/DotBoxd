using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

/// <summary>
/// Regression tests for the "make it bulletproof" review pass:
/// the netstandard2.1 dispose deadlock (the channel must be closed before the read loop is awaited),
/// the <see cref="InstanceRegistry"/> bound validation, and the null-ServiceName protocol guard.
/// </summary>
public sealed class BulletproofReviewRegressionTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task DisposeAsync_CompletesWhenReceiveIgnoresCancellation()
    {
        // Emulates a netstandard2.1 socket whose in-progress read does not observe the cancellation
        // token: the read loop only unblocks once the channel is disposed. DisposeCoreAsync must close
        // the channel before awaiting the read loop, otherwise an idle peer deadlocks on dispose.
        var connection = new CancellationIgnoringConnection();
        var peer = RpcPeer.Over(connection, NewSerializer()).Start();

        await connection.ReceiveParked.WaitAsync(TimeSpan.FromSeconds(2));

        var dispose = peer.DisposeAsync().AsTask();
        var winner = await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(dispose, winner); // a deadlock would let the 5s delay win first
        await dispose;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void InstanceRegistry_RejectsNonPositiveBound(int maxInstances)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new InstanceRegistry(maxInstances));
        Assert.Equal("maxInstances", ex.ParamName);
    }

    [Fact]
    public async Task NullServiceName_AnsweredWithProtocolError_NotInternalError()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        await using var peer = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        // A hostile/malformed envelope can carry a null ServiceName (MessagePack nil). It must map to
        // a clean protocol error, not an ArgumentNullException from the dictionary lookup surfaced as
        // InternalError.
        using var requestFrame = MessageFramer.FrameMessage(
            serializer,
            99,
            MessageType.Request,
            new RpcRequest { MessageId = 99, ServiceName = null!, MethodName = "Whatever" },
            ReadOnlySpan<byte>.Empty);
        await client.SendAsync(requestFrame.Memory);

        using var responseFrame = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(MessageFramer.TryReadFrame(
            responseFrame.Memory,
            out var messageId,
            out var messageType,
            out var envelope,
            out _));
        var response = serializer.Deserialize<RpcResponse>(envelope);

        Assert.Equal(99, messageId);
        Assert.Equal(MessageType.Error, messageType);
        Assert.False(response.IsSuccess);
        Assert.Equal(RpcErrorTypes.ProtocolError, response.ErrorType);
    }

    /// <summary>
    /// A channel whose <see cref="ReceiveAsync"/> ignores the cancellation token and only unblocks on
    /// <see cref="DisposeAsync"/> — mirroring a socket read that does not honor cancellation on
    /// netstandard2.1 runtimes (.NET Framework, Unity/Mono).
    /// </summary>
    private sealed class CancellationIgnoringConnection : IRpcChannel
    {
        private readonly TaskCompletionSource<Payload> _receive =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _parked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "blocking://remote";

        /// <summary>Completes once the read loop has parked inside <see cref="ReceiveAsync"/>.</summary>
        public Task ReceiveParked => _parked.Task;

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            _parked.TrySetResult(true);
            return _receive.Task;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return default;
            }

            _receive.TrySetResult(Payload.Empty);
            return default;
        }
    }
}
