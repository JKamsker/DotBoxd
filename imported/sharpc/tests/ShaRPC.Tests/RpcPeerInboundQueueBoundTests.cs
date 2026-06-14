using System.Buffers;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class RpcPeerInboundQueueBoundTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task WaitQueue_DoesNotReadPastConfiguredCapacity()
    {
        var serializer = NewSerializer();
        await using var connection = new ScriptedConnection();
        var dispatcher = new BlockingDispatcher();

        for (var id = 1; id <= 4; id++)
        {
            connection.Enqueue(CreateRequestFrame(serializer, id));
        }

        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 1,
                    QueueFullMode = ShaRpcQueueFullMode.Wait,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        await dispatcher.FirstEntered.WaitAsync(TimeSpan.FromSeconds(1));
        await connection.WaitForReceiveCountAsync(3, TimeSpan.FromSeconds(1));

        // The read loop is parked enqueuing the 3rd frame into the full (capacity-1) queue, so it
        // never makes a 4th receive attempt. Assert the absence deterministically: the wait fails
        // fast if a regression let the loop read past capacity, instead of always sleeping a fixed delay.
        await Assert.ThrowsAsync<TimeoutException>(
            () => connection.WaitForReceiveAttemptAsync(4, TimeSpan.FromMilliseconds(200)));
        Assert.Equal(3, connection.ReceiveCount);

        dispatcher.Release();
        await connection.WaitForReceiveCountAsync(4, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task DropIncoming_ReleasesDroppedFrame()
    {
        var serializer = NewSerializer();
        await using var connection = new ScriptedConnection();
        var dispatcher = new BlockingDispatcher();
        var first = CreateRequestFrame(serializer, 1);
        var second = CreateRequestFrame(serializer, 2);
        var third = CreateRequestFrame(serializer, 3);
        connection.Enqueue(first);
        connection.Enqueue(second);
        connection.Enqueue(third);

        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 1,
                    QueueFullMode = ShaRpcQueueFullMode.DropIncoming,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        await dispatcher.FirstEntered.WaitAsync(TimeSpan.FromSeconds(1));
        await connection.WaitForReceiveCountAsync(3, TimeSpan.FromSeconds(1));
        await connection.WaitForReceiveAttemptAsync(4, TimeSpan.FromSeconds(1));

        // At least one of the two trailing frames overflowed the capacity-1 queue and was released
        // (disposed) on drop. We assert "at least one" rather than "exactly one": whether one or both
        // drop depends on whether the dispatch worker has drained the queued frame yet, so an
        // exactly-one assertion would be racy. The actively-dispatched frame must NOT be disposed
        // while the handler still holds it.
        Assert.True(IsDisposed(second) || IsDisposed(third));
        Assert.False(IsDisposed(first));

        dispatcher.Release();
    }

    [Fact]
    public async Task WaitQueue_DispatchesConcurrently_UpToMaxConcurrentInboundDispatch()
    {
        var serializer = NewSerializer();
        await using var connection = new ScriptedConnection();
        var dispatcher = new ConcurrencyTrackingDispatcher(maxExpected: 3);

        for (var id = 1; id <= 5; id++)
        {
            connection.Enqueue(CreateRequestFrame(serializer, id, ConcurrencyTrackingDispatcher.Service, "Run"));
        }

        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 5,
                    MaxConcurrentInboundDispatch = 3,
                    QueueFullMode = ShaRpcQueueFullMode.Wait,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        // Exactly maxConcurrency=3 dispatches run at once: the 3rd entering proves concurrency is
        // applied (with serial dispatch this wait would time out), and the 4th cannot enter because
        // all dispatch slots are held — asserted deterministically, not via a delay.
        await dispatcher.TargetReached.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(dispatcher.OverflowEntered.IsCompleted);

        dispatcher.Release();
    }

    [Fact]
    public async Task ByteBudget_ParksReadLoop_WhenInFlightBytesWouldExceedBudget()
    {
        var serializer = NewSerializer();
        await using var connection = new ScriptedConnection();
        var dispatcher = new BlockingDispatcher();

        for (var id = 1; id <= 3; id++)
        {
            connection.Enqueue(CreateRequestFrame(serializer, id));
        }

        // Count cap is generous (100) so it never binds; the 1-byte byte budget binds instead. The
        // first frame is admitted under the "nothing in flight" rule (so a frame larger than the whole
        // budget never deadlocks), and the second frame parks the read loop in the byte gate.
        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 100,
                    MaxInboundBytes = 1,
                    QueueFullMode = ShaRpcQueueFullMode.Wait,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        await dispatcher.FirstEntered.WaitAsync(TimeSpan.FromSeconds(1));
        await connection.WaitForReceiveCountAsync(2, TimeSpan.FromSeconds(1));

        // The read loop is parked admitting frame 2's bytes, so it never reads the 3rd frame. Assert
        // the absence deterministically rather than by sleeping.
        await Assert.ThrowsAsync<TimeoutException>(
            () => connection.WaitForReceiveAttemptAsync(3, TimeSpan.FromMilliseconds(200)));
        Assert.Equal(2, connection.ReceiveCount);

        // Releasing frame 1 frees its bytes, so frame 2 admits and the loop reads the 3rd frame.
        dispatcher.Release();
        await connection.WaitForReceiveCountAsync(3, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RejectInboundCalls_ReturnsExplicitErrorResponse()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        await using var peer = RpcPeer
            .Over(
                serverConnection,
                serializer,
                new RpcPeerOptions
                {
                    RejectInboundCalls = true,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                })
            .Start();

        using var requestFrame = CreateRequestFrame(serializer, 42);
        await client.SendAsync(requestFrame.Memory);

        using var responseFrame = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(MessageFramer.TryReadFrame(
            responseFrame.Memory,
            out var messageId,
            out var messageType,
            out var envelope,
            out var payload));
        var response = serializer.Deserialize<RpcResponse>(envelope);

        Assert.Equal(42, messageId);
        Assert.Equal(MessageType.Error, messageType);
        Assert.Equal(0, payload.Length);
        Assert.False(response.IsSuccess);
        Assert.Equal(RpcErrorTypes.InboundRejected, response.ErrorType);
        Assert.Equal("This peer does not accept inbound calls.", response.ErrorMessage);
    }

    private static Payload CreateRequestFrame(ISerializer serializer, int messageId) =>
        CreateRequestFrame(serializer, messageId, BlockingDispatcher.Service, "Hold");

    private static Payload CreateRequestFrame(ISerializer serializer, int messageId, string service, string method) =>
        MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = messageId,
                ServiceName = service,
                MethodName = method,
            },
            ReadOnlySpan<byte>.Empty);

    private static bool IsDisposed(Payload frame)
    {
        try
        {
            _ = frame.Memory;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private sealed class BlockingDispatcher : IServiceDispatcher
    {
        public const string Service = "Blocking";

        private readonly TaskCompletionSource<bool> _firstEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ServiceName => Service;

        public Task FirstEntered => _firstEntered.Task;

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            _firstEntered.TrySetResult(true);
            await _release.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        public void Release() => _release.TrySetResult(true);
    }

    private sealed class ConcurrencyTrackingDispatcher : IServiceDispatcher
    {
        public const string Service = "Concurrent";

        private readonly int _maxExpected;
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _targetReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _overflowEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;

        public ConcurrencyTrackingDispatcher(int maxExpected) => _maxExpected = maxExpected;

        public string ServiceName => Service;

        /// <summary>Completes once <c>maxExpected</c> dispatches are concurrently active.</summary>
        public Task TargetReached => _targetReached.Task;

        /// <summary>Completes if a dispatch beyond <c>maxExpected</c> ever becomes active.</summary>
        public Task OverflowEntered => _overflowEntered.Task;

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            var active = Interlocked.Increment(ref _active);
            if (active == _maxExpected)
            {
                _targetReached.TrySetResult(true);
            }
            else if (active > _maxExpected)
            {
                _overflowEntered.TrySetResult(true);
            }

            try
            {
                await _release.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public void Release() => _release.TrySetResult(true);
    }
}
