using DotBoxD.Services.Client;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Waves;

/// <summary>
/// Wave 5 coverage tests for protocol validation guards and cancel-frame sender lifecycle.
/// </summary>
public sealed class Wave5_ValidationAndGuardTests
{
    [Fact]
    public void TryValidateInboundHandles_SingleHandle_ZeroStreamId_ReturnsFalse()
    {
        var handles = new[] { new RpcStreamHandle(0, RpcStreamKind.Binary) };

        var result = RpcStreamValidation.TryValidateInboundHandles(handles, out var error);

        Assert.False(result);
        Assert.Contains("zero", error!);
    }

    [Fact]
    public void TryValidateInboundHandles_MultiHandle_ZeroStreamId_ReturnsFalse()
    {
        var handles = new[]
        {
            new RpcStreamHandle(1, RpcStreamKind.Binary),
            new RpcStreamHandle(0, RpcStreamKind.Items),
        };

        var result = RpcStreamValidation.TryValidateInboundHandles(handles, out var error);

        Assert.False(result);
        Assert.Contains("zero", error!);
    }

    [Fact]
    public void TryValidateInboundHandles_MultiHandle_DuplicateStreamId_ReturnsFalse()
    {
        var handles = new[]
        {
            new RpcStreamHandle(42, RpcStreamKind.Binary),
            new RpcStreamHandle(42, RpcStreamKind.Items),
        };

        var result = RpcStreamValidation.TryValidateInboundHandles(handles, out var error);

        Assert.False(result);
        Assert.Contains("Duplicate", error!);
        Assert.Contains("42", error!);
    }

    [Fact]
    public void TryValidateInboundHandles_TooManyHandles_ReturnsFalse()
    {
        var handles = new[]
        {
            new RpcStreamHandle(1, RpcStreamKind.Binary),
            new RpcStreamHandle(2, RpcStreamKind.Items),
            new RpcStreamHandle(3, RpcStreamKind.Binary),
        };

        var result = RpcStreamValidation.TryValidateInboundHandles(handles, maxHandles: 2, out var error);

        Assert.False(result);
        Assert.Contains("Request declares 3 inbound streams", error!);
        Assert.Contains("maximum is 2", error!);
    }

    [Fact]
    public void TryValidateInboundHandles_NonPositiveLimit_Throws()
    {
        var handles = new[] { new RpcStreamHandle(1, RpcStreamKind.Binary) };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => RpcStreamValidation.TryValidateInboundHandles(handles, maxHandles: 0, out _));

        Assert.Equal("maxHandles", ex.ParamName);
    }

    [Fact]
    public void TryValidateInboundHandles_SingleHandle_UnknownKind_ReturnsFalse()
    {
        var handles = new[] { new RpcStreamHandle(1, (RpcStreamKind)99) };

        var result = RpcStreamValidation.TryValidateInboundHandles(handles, out var error);

        Assert.False(result);
        Assert.Contains("Unknown stream kind", error!);
    }

    [Fact]
    public void TryValidateInboundHandles_MultiHandle_UnknownKind_ReturnsFalse()
    {
        var handles = new[]
        {
            new RpcStreamHandle(1, RpcStreamKind.Binary),
            new RpcStreamHandle(2, (RpcStreamKind)77),
        };

        var result = RpcStreamValidation.TryValidateInboundHandles(handles, out var error);

        Assert.False(result);
        Assert.Contains("Unknown stream kind", error!);
    }

    [Fact]
    public void TryValidateInboundHandles_Null_ReturnsTrue()
    {
        var result = RpcStreamValidation.TryValidateInboundHandles(null, out var error);

        Assert.True(result);
        Assert.Null(error);
    }

    [Fact]
    public void TryValidateInboundHandles_Empty_ReturnsTrue()
    {
        var result = RpcStreamValidation.TryValidateInboundHandles(
            Array.Empty<RpcStreamHandle>(), out var error);

        Assert.True(result);
        Assert.Null(error);
    }

    [Fact]
    public void TryValidateInboundHandles_MultiHandle_AllValid_ReturnsTrue()
    {
        var handles = new[]
        {
            new RpcStreamHandle(1, RpcStreamKind.Binary),
            new RpcStreamHandle(2, RpcStreamKind.Items),
        };

        var result = RpcStreamValidation.TryValidateInboundHandles(handles, out var error);

        Assert.True(result);
        Assert.Null(error);
    }

    [Fact]
    public async Task CancelFrameSender_TrySend_AfterStop_DropsFrameSilently()
    {
        var sendCount = 0;
        var sender = new RpcPeerCancelFrameSender(
            (_, _) => { Interlocked.Increment(ref sendCount); return Task.CompletedTask; });

        await sender.StopAsync();

        sender.TrySend(42);

        Assert.Equal(0, sendCount);
    }

    [Fact]
    public async Task CancelFrameSender_TrySend_SaturatedSlots_DropsFrame()
    {
        var gate = new TaskCompletionSource();
        var sendCount = 0;
        var sender = new RpcPeerCancelFrameSender(
            async (_, _) =>
            {
                Interlocked.Increment(ref sendCount);
                await gate.Task;
            });

        for (var i = 0; i < 16; i++)
        {
            sender.TrySend(i);
        }

        Assert.Equal(16, sendCount);

        sender.TrySend(999);
        Assert.Equal(16, sendCount);

        gate.SetResult();
        await sender.StopAsync();
    }

    [Fact]
    public void StreamCompleteFrameReader_OversizedFrame_ReturnsFalse()
    {
        var extraBytes = new byte[] { 0xFF };
        using var frame = MessageFramer.FrameToPayload(1, MessageType.StreamComplete, extraBytes);

        var result = RpcStreamCompleteFrameReader.TryRead(frame, out var streamId);

        Assert.False(result);
        Assert.Equal(0, streamId);
    }

    [Fact]
    public void StreamCompleteFrameReader_ValidFrame_ReturnsTrue()
    {
        using var frame = MessageFramer.FrameToPayload(
            42, MessageType.StreamComplete, ReadOnlySpan<byte>.Empty);

        var result = RpcStreamCompleteFrameReader.TryRead(frame, out var streamId);

        Assert.True(result);
        Assert.Equal(42, streamId);
    }

    [Fact]
    public void StreamCompleteFrameReader_ZeroStreamId_ReturnsFalse()
    {
        using var frame = MessageFramer.FrameToPayload(
            0, MessageType.StreamComplete, ReadOnlySpan<byte>.Empty);

        var result = RpcStreamCompleteFrameReader.TryRead(frame, out _);

        Assert.False(result);
    }

    [Fact]
    public void StreamCompleteFrameReader_WrongMessageType_ReturnsFalse()
    {
        using var frame = MessageFramer.FrameToPayload(
            1, MessageType.StreamItem, ReadOnlySpan<byte>.Empty);

        var result = RpcStreamCompleteFrameReader.TryRead(frame, out _);

        Assert.False(result);
    }

    [Fact]
    public void ValidateOutboundAttachment_Null_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => RpcStreamValidation.ValidateOutboundAttachment(null!));
    }

    [Fact]
    public void ValidateOutboundAttachment_ZeroStreamId_ThrowsProtocolException()
    {
        var stream = new MemoryStream();
        var handle = new RpcStreamHandle(0, RpcStreamKind.Binary);
        var attachment = RpcStreamAttachment.FromStream(handle, stream);

        Assert.Throws<DotBoxD.Services.Exceptions.ServiceProtocolException>(
            () => RpcStreamValidation.ValidateOutboundAttachment(attachment));
    }

    [Fact]
    public void ValidateOutboundAttachments_DuplicateStreamId_ThrowsProtocolException()
    {
        var s1 = new MemoryStream();
        var s2 = new MemoryStream();
        var handle = new RpcStreamHandle(7, RpcStreamKind.Binary);
        var a1 = RpcStreamAttachment.FromStream(handle, s1);
        var a2 = RpcStreamAttachment.FromStream(handle, s2);

        var ex = Assert.Throws<DotBoxD.Services.Exceptions.ServiceProtocolException>(
            () => RpcStreamValidation.ValidateOutboundAttachments(new[] { a1, a2 }));
        Assert.Contains("Duplicate", ex.Message);
    }
}
