using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class StreamingFrameSenderTests
{
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
}
