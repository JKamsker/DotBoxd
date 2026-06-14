using DotBoxd.Services;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Streaming;
using DotBoxd.Codecs.MessagePack;
using Xunit;

namespace DotBoxd.Services.Tests;

/// <summary>
/// Regression: <see cref="RpcRemoteStream.Dispose"/> calls <c>_current.Dispose()</c> before
/// calling <c>_receiver.Cancel()</c>. <c>RpcStreamChunk.Dispose</c> invokes
/// <c>RpcStreamReceiver.ReleaseCredit()</c>, which checks <c>_completed == 0</c>. Because
/// <c>Cancel()</c> has not yet run, <c>_completed</c> is still 0 and a spurious
/// <see cref="MessageType.StreamCredit"/> frame is sent to the remote. The remote may send one
/// more item based on this credit, even though the local side has already abandoned the stream.
///
/// The correct order is: call <c>_receiver.Cancel()</c> first (sets <c>_completed = 1</c>), then
/// dispose the current chunk — <c>ReleaseCredit</c> will then see <c>_completed != 0</c> and skip
/// the credit send.
///
/// These tests are RED: they fail against the current production code.
/// </summary>
public sealed class StreamingRemoteStreamDisposeCreditsTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// After reading one byte from an <see cref="RpcRemoteStream"/>, disposing the stream must NOT
    /// send an additional <see cref="MessageType.StreamCredit"/> frame. The current chunk's credit
    /// must be suppressed because the receiver is being canceled, not returned to the window.
    ///
    /// CURRENTLY FAILS: Dispose sends one extra StreamCredit frame because it disposes the current
    /// chunk (triggering ReleaseCredit) before canceling the receiver (which would set _completed = 1).
    /// </summary>
    [Fact]
    public async Task Dispose_WithCurrentChunk_DoesNotSendExtraStreamCredit()
    {
        var creditFrameCount = 0;

        Task CountingSendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            if (MessageFramer.TryReadFrameHeader(frame, out _, out var type) &&
                type == MessageType.StreamCredit)
            {
                Interlocked.Increment(ref creditFrameCount);
            }

            return Task.CompletedTask;
        }

        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, CountingSendAsync, exceptionTransformer: null);

        var handle = new RpcStreamHandle(streamId: 1, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);

        // RegisterInboundResponse fires WindowSize (4) credit frames synchronously because
        // CountingSendAsync returns Task.CompletedTask. Record the baseline.
        var creditsAfterRegistration = creditFrameCount;

        // Feed one item into the receiver. Ownership of the Payload transfers to the chunk inside
        // the receiver's channel — do NOT wrap in using here.
        var frame = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamItem,
            new byte[] { 0x42 });
        Assert.True(streams.TryAcceptItem(handle.StreamId, frame));

        // Wrap the receiver in RpcRemoteStream and read exactly one byte so that _current is set
        // to the chunk (internal field populated by ReadCoreAsync).
        var remoteStream = new RpcRemoteStream(receiver);
        var buf = new byte[1];
        var bytesRead = await remoteStream.ReadAsync(buf, 0, 1, CancellationToken.None)
            .WaitAsync(TestTimeout);
        Assert.Equal(1, bytesRead);
        Assert.Equal(0x42, buf[0]);

        // No extra credit should have been sent just from reading.
        Assert.Equal(creditsAfterRegistration, creditFrameCount);

        // --- The act that exposes the bug ---
        remoteStream.Dispose();

        // Expected (post-fix): Dispose cancels the receiver first (_completed = 1), then disposes
        // the chunk. ReleaseCredit sees _completed != 0 and skips the send. Credit count unchanged.
        //
        // CURRENTLY FAILS: Dispose disposes the chunk first while _completed == 0, so
        // ReleaseCredit fires one extra StreamCredit, making creditFrameCount == creditsAfterRegistration + 1.
        var creditsAfterDispose = creditFrameCount;
        Assert.Equal(creditsAfterRegistration, creditsAfterDispose);
    }
}
