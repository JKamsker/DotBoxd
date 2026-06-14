using DotBoxd.Services;
using DotBoxd.Services.Exceptions;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Streaming;
using DotBoxd.Codecs.MessagePack;
using Xunit;

namespace DotBoxd.Services.Tests;

/// <summary>
/// Regression for the batch RegisterInbound rollback path.
///
/// When the batch overload fails mid-registration it removes already-registered receivers from
/// _receivers and calls receiver.Abort(). Abort calls Complete() — not Cancel() — which does NOT
/// add a tombstone to _canceledInbound. The remote peer already received initial credits for
/// those receivers before the failure, so it will send items for them. Because there is no
/// tombstone, TryAcceptItem cannot silently consume those late items and returns false — which
/// the frame processor treats as a spurious protocol violation.
///
/// The correct fix is for the rollback path to write a tombstone (e.g. via RemoveCanceledInbound
/// instead of a plain _receivers.TryRemove) for every receiver that was successfully started
/// before the failure.
/// </summary>
public sealed class StreamingBatchRegistrationTombstoneTests
{
    /// <summary>
    /// After a batch RegisterInbound rolls back the first stream (because the second handle
    /// carries the same stream id), TryAcceptItem for the rolled-back stream id must return
    /// true so late items from the remote are silently consumed rather than triggering a
    /// protocol error.
    ///
    /// This test currently FAILS because the rollback calls receiver.Abort() which does NOT
    /// write a tombstone to _canceledInbound. TryAcceptItem therefore returns false.
    /// </summary>
    [Fact]
    public void BatchRegisterInbound_WhenRolledBackDueToDuplicate_LeavesTombstoneForFirstStream()
    {
        // Arrange
        var streams = CreateStreamManager();
        const int streamId = 5;
        var handle = new RpcStreamHandle(streamId, RpcStreamKind.Binary);

        // Two identical handles: the second RegisterInbound call throws
        // "Inbound stream id '5' is already active.", triggering the catch block.
        // The catch block removes the first receiver from _receivers and calls Abort(),
        // but — due to the bug — does NOT call RemoveCanceledInbound, so no tombstone is added.
        var ex = Assert.Throws<DotBoxdRpcProtocolException>(() =>
            streams.RegisterInbound(new[] { handle, handle }, CancellationToken.None));

        Assert.Contains($"'{streamId}' is already active", ex.Message);

        // The receiver was removed by the rollback path; no active receiver exists.
        Assert.Equal(0, streams.InboundReceiverCount);

        // Act — simulate the remote sending a late StreamItem for the rolled-back stream.
        // Payload.Dispose() is idempotent (Interlocked.Exchange guard), so using-var is safe
        // regardless of whether TryAcceptItem consumes the frame (disposes it) or returns false.
        using var frame = MessageFramer.FrameToPayload(
            streamId,
            MessageType.StreamItem,
            new byte[] { 1, 2, 3 });

        var accepted = streams.TryAcceptItem(streamId, frame);

        // Assert — a tombstone must exist so TryAcceptItem silently consumes the item (true).
        // Currently FAILS: returns false because no tombstone was written during rollback.
        Assert.True(accepted,
            $"TryAcceptItem must return true (tombstone consume) for rolled-back stream {streamId}, " +
            "but returned false — no tombstone was added to _canceledInbound during batch rollback.");
    }

    private static RpcStreamManager CreateStreamManager()
    {
        var serializer = new MessagePackRpcSerializer();
        return new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
