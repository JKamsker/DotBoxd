using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests.Coverage;

/// <summary>
/// Round 13: TryAcceptItem Rejected path and RegisterInbound rollback.
/// </summary>
public sealed class Round13_AcceptRejectAndRollbackTests
{
    // ────────────────────────────────────────────────────────────────────
    // BUG: TryAcceptItem returns false when TryAccept returns Rejected
    // (window overflow). This causes the read loop to report a spurious
    // "Unknown stream id" protocol error and double-dispose the frame.
    // TryAcceptItem should return true for Rejected since the frame was
    // already disposed inside TryAccept.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryAcceptItem_WindowOverflow_ReturnsTrue()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null);

        var handle = new RpcStreamHandle(1, RpcStreamKind.Binary);
        streams.RegisterInboundResponse(handle, CancellationToken.None);

        // Fill the window (WindowSize = 4).
        for (var i = 0; i < RpcStreamManager.WindowSize; i++)
        {
            using var item = MessageFramer.FrameToPayload(
                handle.StreamId, MessageType.StreamItem, new byte[] { (byte)i });
            var accepted = streams.TryAcceptItem(handle.StreamId, item);
            Assert.True(accepted, $"Item {i} should be accepted within window.");
        }

        // The 5th item overflows the window → TryAccept returns Rejected.
        // TryAcceptItem should still return true (frame was handled/disposed).
        using var overflow = MessageFramer.FrameToPayload(
            handle.StreamId, MessageType.StreamItem, new byte[] { 0xFF });
        var result = streams.TryAcceptItem(handle.StreamId, overflow);

        Assert.True(result,
            "TryAcceptItem should return true for window overflow (Rejected), " +
            "not false which causes a spurious protocol error.");
    }

    // ────────────────────────────────────────────────────────────────────
    // BUG: RegisterInbound batch rollback calls RemoveCanceledInbound for
    // successfully registered streams, which adds them to _canceledInbound.
    // This is wrong — the streams were never canceled by the remote, so
    // they should not get tombstones. The tombstones corrupt protocol
    // state and prematurely fill the canceled-inbound capacity.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterInbound_BatchRollback_DoesNotAddToCanceledInbound()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null);

        // Pre-register stream ID 3 as an active receiver so the batch
        // registration will fail when it hits the duplicate.
        streams.RegisterInboundResponse(
            new RpcStreamHandle(3, RpcStreamKind.Binary), CancellationToken.None);

        Assert.Equal(0, streams.CanceledInboundCount);

        // Batch: IDs 1 and 2 will register successfully, then ID 3 will
        // throw (duplicate active stream). The rollback should clean up
        // IDs 1 and 2 without adding them to _canceledInbound.
        var handles = new[]
        {
            new RpcStreamHandle(1, RpcStreamKind.Binary),
            new RpcStreamHandle(2, RpcStreamKind.Binary),
            new RpcStreamHandle(3, RpcStreamKind.Binary), // duplicate → triggers rollback
        };

        Assert.ThrowsAny<Exception>(() =>
            streams.RegisterInbound(handles, CancellationToken.None));

        // The rolled-back streams (1 and 2) should NOT be in _canceledInbound.
        // Currently, RemoveCanceledInbound adds them, which is wrong.
        Assert.Equal(0, streams.CanceledInboundCount);
    }

    // ────────────────────────────────────────────────────────────────────
    // Correctness: After a batch rollback, the rolled-back stream IDs
    // should be re-registerable (no lingering state).
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterInbound_AfterBatchRollback_StreamIdsAreReRegisterable()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null);

        // Pre-register stream ID 3.
        streams.RegisterInboundResponse(
            new RpcStreamHandle(3, RpcStreamKind.Binary), CancellationToken.None);

        var handles = new[]
        {
            new RpcStreamHandle(1, RpcStreamKind.Binary),
            new RpcStreamHandle(2, RpcStreamKind.Binary),
            new RpcStreamHandle(3, RpcStreamKind.Binary),
        };

        Assert.ThrowsAny<Exception>(() =>
            streams.RegisterInbound(handles, CancellationToken.None));

        // Stream IDs 1 and 2 should be re-registerable because the
        // rollback cleaned them up properly.
        var r1 = streams.RegisterInboundResponse(
            new RpcStreamHandle(1, RpcStreamKind.Binary), CancellationToken.None);
        var r2 = streams.RegisterInboundResponse(
            new RpcStreamHandle(2, RpcStreamKind.Binary), CancellationToken.None);

        Assert.NotNull(r1);
        Assert.NotNull(r2);

        r1.Complete();
        r2.Complete();
    }
}
