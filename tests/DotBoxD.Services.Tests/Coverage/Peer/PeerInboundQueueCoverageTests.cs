using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Peer.PeerInboundQueueCoverageTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Peer;

/// <summary>
/// Behavioral coverage for the internal inbound request queue's backpressure: the DropIncoming
/// queue-full policy (with and without a byte budget), the disabled-byte-budget admit/release fast
/// paths, and queue teardown while work is in flight. Exercised through the public
/// <see cref="RpcPeer"/> + <see cref="RpcPeerOptions"/> surface plus the shared scripted/in-memory
/// pipe test helpers.
/// </summary>
public sealed class PeerInboundQueueCoverageTests
{
    // Hang-guard ceiling only (used for .WaitAsync(...) and as a never-expected-to-fire
    // RequestTimeout). Kept generous so 2-core CI runners under parallel load don't trip it;
    // negative timeout tests assert against their own small RequestTimeout literals elsewhere.
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(30);

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
                    QueueFullMode = QueueFullMode.DropIncoming,
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

        var queueFull = await ReadFirstQueueFullAsync(client, serializer, ShortTimeout);
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
                    QueueFullMode = QueueFullMode.DropIncoming,
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

        var queueFull = await ReadFirstQueueFullAsync(client, serializer, ShortTimeout);
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
                    QueueFullMode = QueueFullMode.Wait,
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
                    QueueFullMode = QueueFullMode.Wait,
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
                    QueueFullMode = QueueFullMode.Wait,
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

}
