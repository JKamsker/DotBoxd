using DotBoxD.Services.Peer;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Peer;

public sealed class PeerInboundQueueOptionsCoverageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxConcurrentInboundDispatch_NonPositive_Throws(int value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { MaxConcurrentInboundDispatch = value });
        Assert.Equal("MaxConcurrentInboundDispatch", ex.ParamName);
        Assert.Contains("greater than zero", ex.Message);
    }

    [Fact]
    public void MaxConcurrentInboundDispatch_Positive_IsStored()
    {
        var options = new RpcPeerOptions { MaxConcurrentInboundDispatch = 8 };
        Assert.Equal(8, options.MaxConcurrentInboundDispatch);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-100L)]
    public void MaxInboundBytes_NonPositive_Throws(long value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { MaxInboundBytes = value });
        Assert.Equal("MaxInboundBytes", ex.ParamName);
        Assert.Contains("greater than zero", ex.Message);
    }

    [Fact]
    public void MaxInboundBytes_Null_DisablesBound()
    {
        var options = new RpcPeerOptions { MaxInboundBytes = null };
        Assert.Null(options.MaxInboundBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void InboundQueueCapacity_NonPositive_Throws(int value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { InboundQueueCapacity = value });
        Assert.Equal("InboundQueueCapacity", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxPendingRequests_NonPositive_Throws(int value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { MaxPendingRequests = value });
        Assert.Equal("MaxPendingRequests", ex.ParamName);
    }

    [Fact]
    public void QueueFullMode_UndefinedValue_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { QueueFullMode = (QueueFullMode)99 });
        Assert.Equal("QueueFullMode", ex.ParamName);
        Assert.Contains("Unknown queue full mode", ex.Message);
    }

    [Fact]
    public void RequestTimeout_NonPositive_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeerOptions { RequestTimeout = TimeSpan.Zero });
        Assert.Equal("RequestTimeout", ex.ParamName);
    }

    [Fact]
    public void RequestTimeout_InfiniteTimeSpan_IsAccepted()
    {
        var options = new RpcPeerOptions { RequestTimeout = Timeout.InfiniteTimeSpan };
        Assert.Equal(Timeout.InfiniteTimeSpan, options.RequestTimeout);
    }
}
