using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class StreamingInboundHandleLimitTests
{
    [Fact]
    public void TryValidateInboundHandles_ExactLimit_ReturnsTrue()
    {
        var handles = new[]
        {
            new RpcStreamHandle(1, RpcStreamKind.Binary),
            new RpcStreamHandle(2, RpcStreamKind.Items),
        };

        var result = RpcStreamValidation.TryValidateInboundHandles(handles, maxHandles: 2, out var error);

        Assert.True(result);
        Assert.Null(error);
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
    public void TryValidateInboundHandles_DuplicateIdsAtLimit_ReturnsFalse()
    {
        var handles = new[]
        {
            new RpcStreamHandle(1, RpcStreamKind.Binary),
            new RpcStreamHandle(1, RpcStreamKind.Items),
        };

        var result = RpcStreamValidation.TryValidateInboundHandles(handles, maxHandles: 2, out var error);

        Assert.False(result);
        Assert.Equal("Duplicate inbound stream id '1'.", error);
    }

    [Fact]
    public void TryValidateInboundHandles_NonPositiveLimit_Throws()
    {
        var handles = new[] { new RpcStreamHandle(1, RpcStreamKind.Binary) };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => RpcStreamValidation.TryValidateInboundHandles(handles, maxHandles: 0, out _));

        Assert.Equal("maxHandles", ex.ParamName);
    }
}
