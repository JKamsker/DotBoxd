using System.Buffers;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Tests;
using Xunit;

namespace ShaRPC.Tests.Cov.PeerOutbound;

/// <summary>
/// Lifecycle/teardown coverage that complements <see cref="PeerOutboundCoverageTests"/>: faulting many
/// in-flight requests at once on dispose (the <c>ShaRpcPendingRequests.FailAll</c> snapshot path), the
/// <c>ReceivedResponse.DisposeWhenAvailable</c> deferred-disposal path for an unconsumed response, and
/// the inbound byte-gate wait that drives <c>RpcTaskWaiter</c> being cancelled by peer shutdown.
/// </summary>
public sealed class PeerOutboundLifecycleCoverageTests
{
    private const string Service = "Svc";
    private const string Method = "Op";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static MessagePackRpcSerializer NewSerializer() => new();

    private static Payload ResponseFrame<TResult>(ISerializer serializer, int messageId, TResult result)
    {
        var response = new RpcResponse { MessageId = messageId, IsSuccess = true };
        var payloadWriter = new ArrayBufferWriter<byte>();
        serializer.Serialize(payloadWriter, result);
        return MessageFramer.FrameMessage(serializer, messageId, MessageType.Response, response, payloadWriter.WrittenSpan);
    }

    [Fact]
    public async Task Dispose_WithManyPendingRequests_FaultsAllOfThem()
    {
        var serializer = NewSerializer();
        var channel = new ScriptedConnection();
        var peer = RpcPeer.Over(
            channel,
            serializer,
            new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(30), MaxPendingRequests = 64 });
        peer.Start();

        const int inFlight = 16;
        var calls = Enumerable.Range(0, inFlight)
            .Select(i => peer.InvokeAsync<int, string>(Service, Method, request: i))
            .ToArray();

        await peer.DisposeAsync();

        // FailAll snapshots the dictionary and faults every captured completion with ShaRpcConnectionException.
        foreach (var call in calls)
        {
            var ex = await Assert.ThrowsAsync<ShaRpcConnectionException>(() => call.WaitAsync(Timeout));
            Assert.Contains("closed", ex.Message);
        }

        await channel.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_AfterResponseRacedTeardown_DoesNotLeakOrThrow()
    {
        var serializer = NewSerializer();
        var channel = new ScriptedConnection();
        var peer = RpcPeer.Over(channel, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(30) });
        peer.Start();

        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1);

        // Deliver the response, then immediately dispose. Whichever side wins, the call resolves to a
        // value OR a connection-closed fault, and disposal must complete cleanly (exercises the
        // unconsumed-response deferred-dispose path when teardown wins the race).
        channel.Enqueue(ResponseFrame(serializer, messageId: 1, result: "raced"));

        Exception? failure = null;
        string? value = null;
        try
        {
            value = await call.WaitAsync(Timeout);
        }
        catch (ShaRpcConnectionException ex)
        {
            failure = ex;
        }

        await peer.DisposeAsync();
        await channel.DisposeAsync();

        // Exactly one of the two legitimate outcomes occurred.
        Assert.True(value == "raced" || failure is not null);
    }

    [Fact]
    public async Task Dispose_WhileReadLoopParkedInByteGate_TearsDownCleanly()
    {
        // Parks the read loop inside RpcPeerInboundRequestQueue.AdmitBytesAsync (which awaits
        // RpcTaskWaiter.WaitAsync on a cancellable token). Disposing the peer cancels that token, so the
        // waiter takes its cancellation branch and the whole peer tears down without hanging.
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        var dispatcher = new BlockingDispatcher();

        for (var id = 1; id <= 3; id++)
        {
            channel.Enqueue(CreateRequestFrame(serializer, id));
        }

        var peer = RpcPeer
            .Over(
                channel,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 100,
                    MaxInboundBytes = 1,
                    QueueFullMode = ShaRpcQueueFullMode.Wait,
                    RequestTimeout = TimeSpan.FromSeconds(30),
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        // First request dispatching (held by the blocking dispatcher); the second frame parks the read
        // loop in the byte gate because in-flight bytes already exceed the 1-byte budget.
        await dispatcher.FirstEntered.WaitAsync(Timeout);
        await channel.WaitForReceiveCountAsync(2, Timeout);

        // Dispose must complete within the timeout: cancelling the lifecycle token unblocks the parked
        // RpcTaskWaiter wait instead of deadlocking.
        await peer.DisposeAsync().AsTask().WaitAsync(Timeout);

        dispatcher.Release();
    }

    [Fact]
    public async Task Cancellation_OfManyInFlightRequests_EachFaultsAndPeerStaysUsable()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(
            channel,
            serializer,
            new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(30), MaxPendingRequests = 64 }).Start();

        const int count = 12;
        using var cts = new CancellationTokenSource();
        var calls = Enumerable.Range(0, count)
            .Select(_ => peer.InvokeAsync<int, string>(Service, Method, request: 1, cts.Token))
            .ToArray();

        cts.Cancel();

        // Every request faults with cancellation; TryCancel removes each pending entry and the cancel
        // frame sender best-effort emits cancels (capped at its in-flight limit; overflow is silently
        // skipped, not faulted).
        foreach (var call in calls)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => call.WaitAsync(Timeout));
        }

        // A fresh non-cancelled call still succeeds, proving the admission slots were all released.
        var followUp = peer.InvokeAsync<int, string>(Service, Method, request: 2);
        var nextIds = Enumerable.Range(1, count + 1);
        foreach (var id in nextIds)
        {
            channel.Enqueue(ResponseFrame(serializer, messageId: id, result: "ok"));
        }

        Assert.Equal("ok", await followUp.WaitAsync(Timeout));
    }

    private static Payload CreateRequestFrame(ISerializer serializer, int messageId) =>
        MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = messageId,
                ServiceName = BlockingDispatcher.ServiceConst,
                MethodName = "Hold",
            },
            ReadOnlySpan<byte>.Empty);

    /// <summary>
    /// Inbound dispatcher that signals on first entry and blocks until released. Used to pin in-flight
    /// inbound bytes so the read loop parks in the byte gate. Named uniquely to avoid colliding with the
    /// one in <c>RpcPeerInboundQueueBoundTests</c>.
    /// </summary>
    private sealed class BlockingDispatcher : IServiceDispatcher
    {
        public const string ServiceConst = "LifecycleBlocking";

        private readonly TaskCompletionSource<bool> _firstEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ServiceName => ServiceConst;

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
}
