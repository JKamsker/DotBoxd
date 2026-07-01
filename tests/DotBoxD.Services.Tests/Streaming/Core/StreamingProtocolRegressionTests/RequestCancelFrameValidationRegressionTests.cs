using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Client;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;
using Xunit;
using static DotBoxD.Services.Tests.Streaming.Core.StreamingProtocolRegressionTestSupport;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class RequestCancelFrameValidationRegressionTests
{
    [Fact]
    public async Task ZeroIdRequestCancel_ReportsProtocolError()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var protocolErrors = new List<string>();
        var inbound = CreateInbound(serializer, streams);
        var processor = CreateProcessor(serializer, streams, inbound, protocolErrors);
        using var cancelFrame = MessageFramer.FrameToPayload(
            0,
            MessageType.Cancel,
            ReadOnlySpan<byte>.Empty);

        Assert.True(await processor.ShouldDisposeAsync(cancelFrame, CancellationToken.None));

        Assert.Single(protocolErrors, error => error.Contains("Malformed cancel frame."));
    }

    [Fact]
    public async Task RequestCancelWithTrailingPayload_ReportsProtocolErrorWithoutCancelingInboundRequest()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var protocolErrors = new List<string>();
        var inbound = CreateInlineInbound(serializer, streams);
        var dispatcher = new CancelAwareDispatcher();
        inbound.AddDispatcher(dispatcher);
        var processor = CreateProcessor(serializer, streams, inbound, protocolErrors);

        var requestFrame = CreateRequestFrame(serializer, 72);
        Assert.False(await processor.ShouldDisposeAsync(requestFrame, CancellationToken.None));

        await dispatcher.Started.Task.WaitAsync(TestTimeout);

        try
        {
            using var cancelFrame = MessageFramer.FrameToPayload(
                72,
                MessageType.Cancel,
                new byte[] { 1 });

            Assert.True(await processor.ShouldDisposeAsync(cancelFrame, CancellationToken.None));

            Assert.Single(protocolErrors, error => error.Contains("Malformed cancel frame."));
            Assert.False(dispatcher.Canceled.Task.IsCompleted);
        }
        finally
        {
            await inbound.StopAsync().WaitAsync(TestTimeout);
        }
    }

    private static RpcPeerFrameProcessor CreateProcessor(
        MessagePackRpcSerializer serializer,
        RpcStreamManager streams,
        RpcPeerInboundDispatcher inbound,
        List<string> protocolErrors)
    {
        var outboundInvoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions(),
            ensureStarted: static () => { },
            SendNoopAsync,
            streams);

        return new RpcPeerFrameProcessor(
            inbound,
            outboundInvoker,
            streams,
            (id, type, message, _) => protocolErrors.Add($"{id}:{type}:{message}"));
    }

    private static RpcPeerInboundDispatcher CreateInlineInbound(
        MessagePackRpcSerializer serializer,
        RpcStreamManager streams) =>
        new(
            serializer,
            new RpcPeerOptions { InboundQueueCapacity = null, RequestTimeout = TestTimeout },
            streams,
            SendNoopAsync,
            protocolError: static (_, _, _, _) => { },
            dispatchError: static (_, _) => { });

    private static Payload CreateRequestFrame(MessagePackRpcSerializer serializer, int messageId) =>
        MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = messageId,
                ServiceName = CancelAwareDispatcher.Service,
                MethodName = "Wait",
            },
            ReadOnlySpan<byte>.Empty);

    private sealed class CancelAwareDispatcher : IServiceDispatcher
    {
        public const string Service = "CancelAwareInbound";

        public string ServiceName => Service;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Canceled.TrySetResult();
                throw;
            }
        }
    }
}
