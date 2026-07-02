using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;
using Xunit;
using static DotBoxD.Services.Tests.Streaming.Core.StreamingProtocolRegressionTestSupport;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class RpcRequestFrameValidationRegressionTests
{
    [Fact]
    public async Task ZeroIdRequest_ReturnsProtocolErrorWithoutLeakingRequest()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        MessageType? sentType = null;
        var protocolErrors = new List<string>();
        var inbound = new RpcPeerInboundDispatcher(
            serializer,
            new RpcPeerOptions(),
            streams,
            (frame, ct) =>
            {
                Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
                sentType = type;
                return Task.CompletedTask;
            },
            (id, type, message, _) => protocolErrors.Add($"{id}:{type}:{message}"),
            dispatchError: static (_, _) => { });
        using var frame = MessageFramer.FrameMessage(
            serializer,
            0,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = 0,
                ServiceName = "Svc",
                MethodName = "Upload",
            },
            ReadOnlySpan<byte>.Empty);

        try
        {
            var accepted = await inbound.AcceptRequestAsync(frame, 0, CancellationToken.None);

            Assert.False(accepted);
            Assert.Equal(MessageType.Error, sentType);
            Assert.Single(protocolErrors, error => error.Contains("message id"));
            Assert.Equal(0, inbound.ActiveInboundCount);
            Assert.Equal(0, streams.InboundReceiverCount);
        }
        finally
        {
            await inbound.StopAsync().WaitAsync(TestTimeout);
        }
    }
}
