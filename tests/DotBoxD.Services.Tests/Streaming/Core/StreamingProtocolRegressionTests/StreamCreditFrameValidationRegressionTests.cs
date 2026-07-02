using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Client;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using Xunit;
using static DotBoxD.Services.Tests.Streaming.Core.StreamingProtocolRegressionTestSupport;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class StreamCreditFrameValidationRegressionTests
{
    [Fact]
    public async Task ZeroIdStreamCredit_ReportsProtocolError()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var protocolErrors = new List<string>();
        var processor = CreateProcessor(serializer, streams, protocolErrors);
        using var credit = RpcRawFrame.FrameInt32(0, MessageType.StreamCredit, 1);

        Assert.True(await processor.ShouldDisposeAsync(credit, CancellationToken.None));

        Assert.Single(protocolErrors, error => error.Contains("Malformed stream credit frame."));
        Assert.Equal(0, streams.PendingCreditCount);
    }

    [Fact]
    public void TryAddCredit_RejectsZeroStreamId()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        using var credit = RpcRawFrame.FrameInt32(0, MessageType.StreamCredit, 1);

        Assert.False(streams.TryAddCredit(credit));
        Assert.Equal(0, streams.PendingCreditCount);
    }

    private static RpcPeerFrameProcessor CreateProcessor(
        MessagePackRpcSerializer serializer,
        RpcStreamManager streams,
        List<string> protocolErrors)
    {
        var inbound = CreateInbound(serializer, streams);
        var outbound = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions(),
            ensureStarted: static () => { },
            SendNoopAsync,
            streams);

        return new RpcPeerFrameProcessor(
            inbound,
            outbound,
            streams,
            (id, type, message, _) => protocolErrors.Add($"{id}:{type}:{message}"));
    }
}
