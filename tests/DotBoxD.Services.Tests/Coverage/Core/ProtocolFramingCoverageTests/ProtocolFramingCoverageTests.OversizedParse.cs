using System.Buffers.Binary;
using DotBoxD.Services.Protocol;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed partial class MessageFramerCoverageTests
{
    [Fact]
    public void TryReadFrameHeader_DeclaredLengthOneByteAboveMaxMessageSize_ReturnsFalse()
    {
        var frame = CreateOversizedRequestFrame();

        var validation = Assert.Throws<InvalidDataException>(() => MessageFramer.ValidateOutgoingFrame(frame));
        var ok = MessageFramer.TryReadFrameHeader(frame, out var messageId, out var type);

        Assert.Contains((MessageFramer.MaxMessageSize + 1).ToString(), validation.Message);
        Assert.False(ok);
        Assert.Equal(0, messageId);
        Assert.Equal(default, type);
    }

    [Fact]
    public void TryReadFrame_DeclaredLengthOneByteAboveMaxMessageSize_ReturnsFalse()
    {
        var frame = CreateOversizedRequestFrame();

        var validation = Assert.Throws<InvalidDataException>(() => MessageFramer.ValidateOutgoingFrame(frame));
        var ok = MessageFramer.TryReadFrame(frame, out var messageId, out var type, out var envelope, out var payload);

        Assert.Contains((MessageFramer.MaxMessageSize + 1).ToString(), validation.Message);
        Assert.False(ok);
        Assert.Equal(0, messageId);
        Assert.Equal(default, type);
        Assert.True(envelope.IsEmpty);
        Assert.True(payload.IsEmpty);
    }

    private static byte[] CreateOversizedRequestFrame()
    {
        var frame = new byte[MessageFramer.MaxMessageSize + 1];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), frame.Length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), 123);
        frame[8] = (byte)MessageType.Request;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(MessageFramer.HeaderSize, 4), 0);
        return frame;
    }
}
