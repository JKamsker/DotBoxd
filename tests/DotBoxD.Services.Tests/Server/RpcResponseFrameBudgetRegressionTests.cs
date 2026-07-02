using System.Buffers;
using System.Collections.Concurrent;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Server;

public sealed class RpcResponseFrameBudgetRegressionTests
{
    [Fact]
    public async Task OversizedDispatcherOutput_ReturnsErrorBeforeCompletingOversizedResponse()
    {
        var serializer = new MessagePackRpcSerializer();
        var dispatcher = new OversizedOutputDispatcher();
        var dispatchers = new ConcurrentDictionary<string, IServiceDispatcher>();
        Assert.True(dispatchers.TryAdd(dispatcher.ServiceName, dispatcher));
        var builder = new RpcDispatchResponseBuilder(serializer, dispatchers);
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var streaming = new RpcStreamingContext(streams, serializer, CancellationToken.None);

        using var result = await builder.BuildAsync(
            new RpcRequest
            {
                MessageId = 42,
                ServiceName = dispatcher.ServiceName,
                MethodName = "Big",
            },
            messageId: 42,
            ReadOnlyMemory<byte>.Empty,
            new InstanceRegistry(),
            streaming,
            CancellationToken.None);

        Assert.True(MessageFramer.TryReadFrame(
            result.FrameMemory,
            out _,
            out var messageType,
            out var envelope,
            out _));
        Assert.Equal(MessageType.Error, messageType);
        var response = serializer.Deserialize<RpcResponse>(envelope);
        Assert.False(response.IsSuccess);
        Assert.Equal(RpcErrorTypes.InternalError, response.ErrorType);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    private sealed class OversizedOutputDispatcher : IServiceDispatcher, INonStreamingServiceDispatcher
    {
        public string ServiceName => "OversizedOutput";

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            var span = output.GetSpan(MessageFramer.MaxMessageSize);
            span[0] = 0;
            output.Advance(MessageFramer.MaxMessageSize);
            return Task.CompletedTask;
        }

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            IRpcStreamingContext streaming,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
