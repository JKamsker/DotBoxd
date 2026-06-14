using DotBoxd.Services;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Streaming;
using DotBoxd.Codecs.MessagePack;
using Xunit;

namespace DotBoxd.Services.Tests;

/// <summary>
/// Regression for <see cref="RpcStreamManager.Stop"/> not writing tombstones for aborted receivers.
///
/// <see cref="RpcStreamManager.Stop"/> calls <c>receiver.Abort()</c> on every active inbound
/// receiver. <c>Abort</c> calls <c>Complete()</c> + <c>DrainChunks()</c> but does NOT add a
/// tombstone to <c>_canceledInbound</c> and does NOT remove the receiver from <c>_receivers</c>.
/// <c>Stop</c> then calls <c>_canceledInbound.Clear()</c>, wiping any existing tombstones.
///
/// The combined effect is:
/// <list type="bullet">
///   <item>Receivers remain in <c>_receivers</c> after Stop.</item>
///   <item>No tombstone exists in <c>_canceledInbound</c> for those receivers.</item>
/// </list>
///
/// If an in-flight <c>StreamItem</c> frame arrives for an aborted stream (a normal race during
/// connection teardown), <see cref="RpcStreamManager.TryAcceptItem"/> finds the receiver
/// (<c>_receivers</c> hit), calls <c>receiver.TryAccept()</c> which returns <c>false</c>
/// because <c>_completed != 0</c>, then checks <c>_canceledInbound.TryConsumeItem()</c> which
/// also returns <c>false</c> because no tombstone was written. The method returns <c>false</c>
/// and the caller raises a spurious protocol error.
///
/// Compare with <see cref="RpcStreamReceiver.Cancel"/> which calls
/// <c>TryCompleteForCancel</c> → <c>_manager.RemoveCanceledInbound</c> → <c>_canceledInbound.Add</c>,
/// ensuring late items are silently consumed.
///
/// The fix must ensure that <c>Stop</c>'s abort path writes a tombstone for every aborted
/// receiver (e.g. via <c>RemoveCanceledInbound</c>) so that <c>TryAcceptItem</c> returns
/// <c>true</c> and silently disposes any late frames.
/// </summary>
public sealed class StreamingStopAbortTombstoneTests
{
    /// <summary>
    /// After <see cref="RpcStreamManager.Stop"/> aborts a receiver, a late
    /// <c>StreamItem</c> frame arriving for that stream must be silently consumed
    /// (<see cref="RpcStreamManager.TryAcceptItem"/> returns <c>true</c>) rather than
    /// triggering a spurious protocol error (<c>false</c>).
    ///
    /// Currently FAILS because <c>Abort</c> does not add a tombstone to
    /// <c>_canceledInbound</c> and <c>Stop</c> calls <c>_canceledInbound.Clear()</c>,
    /// leaving no tombstone to catch the late item.
    /// </summary>
    [Fact]
    public void Stop_LeavesTombstone_SoLateItemIsSilentlyConsumed()
    {
        // Arrange
        var streams = CreateStreamManager();
        const int streamId = 99;
        var handle = new RpcStreamHandle(streamId, RpcStreamKind.Binary);

        streams.RegisterInboundResponse(handle, CancellationToken.None);

        // Stop aborts the receiver via Abort() — no tombstone is written, no removal from
        // _receivers, and _canceledInbound.Clear() is called.
        streams.Stop();

        // Simulate an in-flight StreamItem frame that races with connection teardown.
        // using-var is required because TryAcceptItem returns false (the bug), meaning the
        // caller owns the frame and must dispose it; Payload.Dispose() is idempotent so
        // this is safe even if the bug is fixed and the frame IS consumed.
        using var frame = MessageFramer.FrameToPayload(
            streamId,
            MessageType.StreamItem,
            new byte[] { 1, 2 });

        // Act
        var accepted = streams.TryAcceptItem(streamId, frame);

        // Assert — TryAcceptItem must return true so the frame is silently consumed and no
        // spurious protocol error is raised. Currently FAILS (returns false) because:
        //   1. The receiver is still in _receivers (Abort does not remove it, Stop does not clear
        //      the dictionary), so _receivers.TryGetValue succeeds.
        //   2. receiver.TryAccept returns false (_completed != 0 after Abort).
        //   3. _canceledInbound.TryConsumeItem returns false (no tombstone — Abort never added
        //      one and _canceledInbound.Clear() wiped everything else).
        Assert.True(accepted,
            $"TryAcceptItem must return true (tombstone consume) for stream {streamId} " +
            "that was aborted by Stop(), but returned false — " +
            "no tombstone was added to _canceledInbound during Stop's abort path.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static RpcStreamManager CreateStreamManager()
    {
        var serializer = new MessagePackRpcSerializer();
        return new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
