using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests.Coverage;

/// <summary>
/// Round 13: TryAcceptItem Rejected path bug.
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

        // The 5th item overflows the window -> TryAccept returns Rejected.
        // TryAcceptItem should still return true (frame was handled/disposed).
        using var overflow = MessageFramer.FrameToPayload(
            handle.StreamId, MessageType.StreamItem, new byte[] { 0xFF });
        var result = streams.TryAcceptItem(handle.StreamId, overflow);

        Assert.True(result,
            "TryAcceptItem should return true for window overflow (Rejected), " +
            "not false which causes a spurious protocol error.");
    }
}
