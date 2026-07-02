using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class StreamingFrameSenderTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StreamControlFrames_UseOwnedFrameSender_WhenAvailable()
    {
        var sent = new List<(int StreamId, MessageType Type, int? Credit)>();
        var memorySends = 0;
        var streams = new RpcStreamManager(
            new MessagePackRpcSerializer(),
            (_, _) =>
            {
                memorySends++;
                return Task.CompletedTask;
            },
            exceptionTransformer: null,
            SendFrameAsync);

        await streams.SendStreamCompleteAsync(101, CancellationToken.None);
        await streams.SendCancelAsync(202, CancellationToken.None);
        await streams.SendCreditAsync(303, 7, CancellationToken.None);

        Assert.Equal(0, memorySends);
        Assert.Equal(
            [
                (101, MessageType.StreamComplete, null),
                (202, MessageType.StreamCancel, null),
                (303, MessageType.StreamCredit, 7),
            ],
            sent);

        ValueTask SendFrameAsync(PooledBufferWriter frame, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                MessageFramer.ValidateOutgoingFrame(frame.WrittenSpan);
                Assert.True(MessageFramer.TryReadFrameHeader(frame.WrittenMemory, out var id, out var type));
                int? credit = null;
                if (type == MessageType.StreamCredit)
                {
                    Assert.True(RpcRawFrame.TryReadInt32(frame.WrittenMemory, out var count));
                    credit = count;
                }

                sent.Add((id, type, credit));
                return default;
            }
            finally
            {
                frame.Dispose();
            }
        }
    }

    [Fact]
    public async Task StreamItem_WhenRawFrameExceedsMaxMessageSize_ThrowsBeforeSending()
    {
        var sendCalled = false;
        int? sentLength = null;
        var streams = new RpcStreamManager(
            new MessagePackRpcSerializer(),
            (_, _) => Task.CompletedTask,
            exceptionTransformer: null,
            SendFrameAsync);
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        await using var outbound = streams.RegisterOutbound(
            RpcStreamAttachment.FromStream(handle, new MemoryStream()),
            CancellationToken.None);
        using var credit = RpcRawFrame.FrameInt32(handle.StreamId, MessageType.StreamCredit, 1);
        var oversizedPayload = new byte[MessageFramer.MaxMessageSize + 1 - MessageFramer.HeaderSize];

        Assert.True(streams.TryAddCredit(credit));
        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => streams.SendStreamItemAsync(handle.StreamId, oversizedPayload, CancellationToken.None)
                .WaitAsync(Timeout));

        Assert.Contains("Invalid DotBoxD frame length", ex.Message);
        Assert.Contains((MessageFramer.MaxMessageSize + 1).ToString(), ex.Message);
        Assert.False(sendCalled, $"Frame sender was called with frame length {sentLength}.");

        ValueTask SendFrameAsync(PooledBufferWriter frame, CancellationToken ct)
        {
            sendCalled = true;
            sentLength = frame.WrittenCount;
            frame.Dispose();
            return default;
        }
    }
}
