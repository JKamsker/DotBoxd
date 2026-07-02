using System.Buffers.Binary;
using DotBoxD.Services.Protocol;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed partial class MessageFramerCoverageTests
{
    private const byte UndefinedMessageTypeByte = 0x7F;

    [Fact]
    public void TryReadFrameHeader_UndefinedMessageType_ReturnsFalse()
    {
        var frame = BuildUndefinedMessageTypeFrame(includeEnvelopeLength: false);

        var ok = MessageFramer.TryReadFrameHeader(frame, out var id, out var type);

        Assert.False(ok);
        Assert.Equal(0, id);
        Assert.Equal(default, type);
    }

    [Fact]
    public void TryReadFrame_UndefinedMessageType_ReturnsFalse()
    {
        var frame = BuildUndefinedMessageTypeFrame(includeEnvelopeLength: true);

        var ok = MessageFramer.TryReadFrame(frame, out var id, out var type, out var envelope, out var payload);

        Assert.False(ok);
        Assert.Equal(0, id);
        Assert.Equal(default, type);
        Assert.True(envelope.IsEmpty);
        Assert.True(payload.IsEmpty);
    }

    [Fact]
    public async Task ReadMessageAsync_UndefinedMessageType_ThrowsInvalidDataException()
    {
        var frame = BuildUndefinedMessageTypeFrame(includeEnvelopeLength: false);
        using var stream = new MemoryStream(frame);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => MessageFramer.ReadMessageAsync(stream).AsTaskWithTimeout(Timeout));

        Assert.Contains("message type", ex.Message);
        Assert.Contains("0x7F", ex.Message);
    }

    private static byte[] BuildUndefinedMessageTypeFrame(bool includeEnvelopeLength)
    {
        var length = MessageFramer.HeaderSize;
        if (includeEnvelopeLength)
        {
            length += MessageFramer.EnvelopeLengthSize;
        }

        var frame = new byte[length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), frame.Length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), 42);
        frame[8] = UndefinedMessageTypeByte;

        return frame;
    }
}
