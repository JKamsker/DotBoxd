using System.Buffers;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Tests;
using Xunit;

namespace ShaRPC.Tests.Cov.PeerInbound;

/// <summary>
/// Behavioral coverage for the internal inbound request queue's backpressure: the DropIncoming
/// queue-full policy (with and without a byte budget), the disabled-byte-budget admit/release fast
/// paths, and queue teardown while work is in flight. Exercised through the public
/// <see cref="RpcPeer"/> + <see cref="RpcPeerOptions"/> surface plus the shared scripted/in-memory
/// pipe test helpers.
/// </summary>
public sealed class PeerInboundQueueCoverageTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(5);

    private static MessagePackRpcSerializer NewSerializer() => new();

    // ---- DropIncoming, byte budget disabled (TryAdmitBytes long.MaxValue: 141-142) -------------

    [Fact]
    public async Task DropIncoming_WithByteBudgetDisabled_RepliesQueueFullForOverflow()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        var dispatcher = new BlockingDispatcher();

        await using var peer = RpcPeer
            .Over(
                serverConnection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 1,
                    MaxInboundBytes = null, // byte budget disabled -> TryAdmitBytes short-circuits true
                    QueueFullMode = ShaRpcQueueFullMode.DropIncoming,
                    RequestTimeout = ShortTimeout,
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        // First call occupies the single dispatch slot and blocks; subsequent calls overflow the
        // capacity-1 queue and are shed with an explicit QueueFull error so callers fail fast.
        await SendRequestAsync(client, serializer, 1, BlockingDispatcher.Service, "Hold");
        await dispatcher.FirstEntered.WaitAsync(ShortTimeout);

        await SendRequestAsync(client, serializer, 2, BlockingDispatcher.Service, "Hold");
        await SendRequestAsync(client, serializer, 3, BlockingDispatcher.Service, "Hold");

        var queueFull = await ReadFirstQueueFullAsync(client, serializer);
        Assert.Equal(RpcErrorTypes.QueueFull, queueFull.ErrorType);
        Assert.Contains("queue is full", queueFull.ErrorMessage);

        dispatcher.Release();
    }

    // ---- DropIncoming, byte budget exceeded (TryAdmitBytes false branch: 155) -------------------

    [Fact]
    public async Task DropIncoming_WhenByteBudgetExceeded_RepliesQueueFull()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        var dispatcher = new BlockingDispatcher();

        await using var peer = RpcPeer
            .Over(
                serverConnection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 100, // count never binds; the 1-byte budget binds instead
                    MaxInboundBytes = 1,
                    QueueFullMode = ShaRpcQueueFullMode.DropIncoming,
                    RequestTimeout = ShortTimeout,
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        // Frame 1 admits under the "nothing in flight" rule and occupies the dispatch slot. Frame 2's
        // bytes cannot be admitted (budget already non-zero), so TryAdmitBytes returns false and the
        // request is dropped with a QueueFull reply.
        await SendRequestAsync(client, serializer, 1, BlockingDispatcher.Service, "Hold");
        await dispatcher.FirstEntered.WaitAsync(ShortTimeout);

        await SendRequestAsync(client, serializer, 2, BlockingDispatcher.Service, "Hold");

        var queueFull = await ReadFirstQueueFullAsync(client, serializer);
        Assert.Equal(RpcErrorTypes.QueueFull, queueFull.ErrorType);
        Assert.Equal(2, queueFull.MessageId);

        dispatcher.Release();
    }

    // ---- Wait mode, byte budget disabled: AdmitBytesAsync / ReleaseBytes fast paths (162-163, 191-192)

    [Fact]
    public async Task WaitQueue_WithByteBudgetDisabled_DispatchesSuccessfully()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();

        await using var server = RpcPeer
            .Over(
                serverConnection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 4,
                    MaxInboundBytes = null, // disabled: AdmitBytesAsync and ReleaseBytes both fast-return
                    QueueFullMode = ShaRpcQueueFullMode.Wait,
                    RequestTimeout = ShortTimeout,
                })
            .Provide((IServiceDispatcher)new EchoNumberDispatcher())
            .Start();

        await using var client = RpcPeer
            .Over(clientConnection, serializer, new RpcPeerOptions { RequestTimeout = ShortTimeout })
            .Start();

        // A normal round trip with the byte budget disabled drives admit (no wait) and release (no
        // signal) through their fast paths while still producing a correct response.
        var result = await client
            .InvokeAsync<int, int>(EchoNumberDispatcher.Service, "Echo", 1234)
            .WaitAsync(ShortTimeout);

        Assert.Equal(1234, result);
    }

    // ---- Wait-mode queue parks the read loop when full (DispatchAsync writer/slot handoff 244-249) -

    [Fact]
    public async Task WaitQueue_DrainsRemainingItems_AfterDispatcherUnblocks()
    {
        var serializer = NewSerializer();
        await using var connection = new ScriptedConnection();
        var dispatcher = new CountingBlockingDispatcher(unblockAfter: 3);

        for (var id = 1; id <= 3; id++)
        {
            connection.Enqueue(CreateRequestFrame(serializer, id, CountingBlockingDispatcher.Service, "Hold"));
        }

        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 4,
                    MaxConcurrentInboundDispatch = 1,
                    QueueFullMode = ShaRpcQueueFullMode.Wait,
                    RequestTimeout = ShortTimeout,
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        // All three are read into the queue; the serial dispatcher processes them one at a time.
        // Once the third has been dispatched, the worker loops back, the writer is still open, and the
        // queue eventually empties — exercising the WaitToRead/TryRead handoff including the slot-return
        // path when no item is ready for an acquired slot.
        await dispatcher.AllDispatched.WaitAsync(ShortTimeout);
        Assert.Equal(3, dispatcher.DispatchedCount);
    }

    // ---- Dispose while a queued dispatch is in flight: StopAsync drains it (queue teardown) ------

    [Fact]
    public async Task Dispose_WithInFlightQueuedDispatch_DrainsCleanly()
    {
        var serializer = NewSerializer();
        var connection = new ScriptedConnection();
        var dispatcher = new BlockingDispatcher();

        connection.Enqueue(CreateRequestFrame(serializer, 1, BlockingDispatcher.Service, "Hold"));
        connection.Enqueue(CreateRequestFrame(serializer, 2, BlockingDispatcher.Service, "Hold"));

        var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 4,
                    MaxConcurrentInboundDispatch = 1,
                    QueueFullMode = ShaRpcQueueFullMode.Wait,
                    RequestTimeout = TimeSpan.FromMinutes(5),
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        await dispatcher.FirstEntered.WaitAsync(ShortTimeout);

        // Dispose cancels the queue CTS while one dispatch is parked and a second sits queued. StopAsync
        // completes the writer, observes the dispatch worker, drains the queued item, and disposes the
        // slot semaphore. The parked handler's await throws on cancellation; teardown stays clean.
        await peer.DisposeAsync().AsTask().WaitAsync(ShortTimeout);
        await connection.DisposeAsync();

        Assert.False(peer.IsConnected);
    }

    // ---------------- RpcPeerOptions validation ----------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxConcurrentInboundDispatch_NonPositive_Throws(int value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { MaxConcurrentInboundDispatch = value });
        Assert.Equal("MaxConcurrentInboundDispatch", ex.ParamName);
        Assert.Contains("greater than zero", ex.Message);
    }

    [Fact]
    public void MaxConcurrentInboundDispatch_Positive_IsStored()
    {
        var options = new RpcPeerOptions { MaxConcurrentInboundDispatch = 8 };
        Assert.Equal(8, options.MaxConcurrentInboundDispatch);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-100L)]
    public void MaxInboundBytes_NonPositive_Throws(long value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { MaxInboundBytes = value });
        Assert.Equal("MaxInboundBytes", ex.ParamName);
        Assert.Contains("greater than zero", ex.Message);
    }

    [Fact]
    public void MaxInboundBytes_Null_DisablesBound()
    {
        var options = new RpcPeerOptions { MaxInboundBytes = null };
        Assert.Null(options.MaxInboundBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void InboundQueueCapacity_NonPositive_Throws(int value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { InboundQueueCapacity = value });
        Assert.Equal("InboundQueueCapacity", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxPendingRequests_NonPositive_Throws(int value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { MaxPendingRequests = value });
        Assert.Equal("MaxPendingRequests", ex.ParamName);
    }

    [Fact]
    public void QueueFullMode_UndefinedValue_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { QueueFullMode = (ShaRpcQueueFullMode)99 });
        Assert.Equal("QueueFullMode", ex.ParamName);
        Assert.Contains("Unknown queue full mode", ex.Message);
    }

    [Fact]
    public void RequestTimeout_NonPositive_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { RequestTimeout = TimeSpan.Zero });
        Assert.Equal("RequestTimeout", ex.ParamName);
    }

    [Fact]
    public void RequestTimeout_InfiniteTimeSpan_IsAccepted()
    {
        var options = new RpcPeerOptions { RequestTimeout = Timeout.InfiniteTimeSpan };
        Assert.Equal(Timeout.InfiniteTimeSpan, options.RequestTimeout);
    }

    // ---------------- Helpers ----------------

    private static async Task SendRequestAsync(
        IRpcChannel channel, ISerializer serializer, int messageId, string service, string method)
    {
        using var frame = CreateRequestFrame(serializer, messageId, service, method);
        await channel.SendAsync(frame.Memory).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads frames from <paramref name="channel"/> until it observes an Error frame carrying the
    /// QueueFull error type, returning a small decoded view. Bounded by a deadline so a regression that
    /// never sheds fails fast instead of hanging.
    /// </summary>
    private static async Task<DecodedError> ReadFirstQueueFullAsync(IRpcChannel channel, ISerializer serializer)
    {
        var deadline = DateTime.UtcNow + ShortTimeout;
        while (DateTime.UtcNow < deadline)
        {
            using var frame = await channel.ReceiveAsync().WaitAsync(ShortTimeout);
            if (frame.Length == 0)
            {
                break;
            }

            if (!MessageFramer.TryReadFrame(
                frame.Memory, out var messageId, out var messageType, out var envelope, out _))
            {
                continue;
            }

            if (messageType != MessageType.Error)
            {
                continue;
            }

            var response = serializer.Deserialize<RpcResponse>(envelope);
            if (response.ErrorType == RpcErrorTypes.QueueFull)
            {
                return new DecodedError(messageId, response.ErrorType, response.ErrorMessage);
            }
        }

        throw new TimeoutException("No QueueFull error frame was observed.");
    }

    private static Payload CreateRequestFrame(ISerializer serializer, int messageId, string service, string method) =>
        MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Request,
            new RpcRequest { MessageId = messageId, ServiceName = service, MethodName = method },
            ReadOnlySpan<byte>.Empty);

    private readonly record struct DecodedError(int MessageId, string? ErrorType, string? ErrorMessage);

    private sealed class EchoNumberDispatcher : IServiceDispatcher
    {
        public const string Service = "EchoNumber";

        public string ServiceName => Service;

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            var value = serializer.Deserialize<int>(payload);
            serializer.Serialize(output, value);
            return Task.CompletedTask;
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

    /// <summary>
    /// A serial dispatcher that completes each call immediately, counts dispatches, and signals once a
    /// target count has been reached. Drives the queue's WaitToRead/TryRead/slot-handoff loop across
    /// several items without ever parking.
    /// </summary>
    private sealed class CountingBlockingDispatcher : IServiceDispatcher
    {
        public const string Service = "Counting";

        private readonly int _unblockAfter;
        private readonly TaskCompletionSource<bool> _allDispatched =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public CountingBlockingDispatcher(int unblockAfter) => _unblockAfter = unblockAfter;

        public string ServiceName => Service;

        public Task AllDispatched => _allDispatched.Task;

        public int DispatchedCount => Volatile.Read(ref _count);

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _count) >= _unblockAfter)
            {
                _allDispatched.TrySetResult(true);
            }

            return Task.CompletedTask;
        }
    }
}
