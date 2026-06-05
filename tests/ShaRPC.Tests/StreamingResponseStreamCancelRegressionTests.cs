using System.Buffers;
using System.Runtime.CompilerServices;
using ShaRPC.Core;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class StreamingResponseStreamCancelRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task EarlyStreamCancelBeforeResponseSenderRegistration_ReleasesRequest()
    {
        var serializer = new MessagePackRpcSerializer();
        RpcStreamManager? streams = null;
        var responseSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        streams = new RpcStreamManager(serializer, SendAndCancelResponseStreamAsync, exceptionTransformer: null);
        var dispatcher = new CanceledResponseDispatcher();
        var inbound = new RpcPeerInboundDispatcher(
            serializer,
            new RpcPeerOptions { InboundQueueCapacity = null },
            streams,
            SendAndCancelResponseStreamAsync,
            protocolError: static (_, _, _, _) => { },
            dispatchError: static (_, _) => { });
        inbound.AddDispatcher(dispatcher);
        var request = MessageFramer.FrameMessage(
            serializer,
            41,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = 41,
                ServiceName = dispatcher.ServiceName,
                MethodName = "Go",
            },
            ReadOnlySpan<byte>.Empty);

        Assert.True(await inbound.AcceptRequestAsync(request, 41, CancellationToken.None));
        await responseSent.Task.WaitAsync(Timeout);
        await WaitUntilAsync(() => inbound.ActiveInboundCount == 0);

        Assert.Equal(0, streams!.OutboundSenderCount);
        Assert.False(dispatcher.ProducerStarted.IsCompleted);

        Task SendAndCancelResponseStreamAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            Assert.True(MessageFramer.TryReadFrame(
                frame,
                out _,
                out var type,
                out var envelope,
                out _));
            if (type == MessageType.Response)
            {
                var response = serializer.Deserialize<RpcResponse>(envelope);
                if (response.Stream is not { } handle)
                {
                    throw new InvalidOperationException("Expected a streamed response.");
                }

                streams!.CancelOutbound(handle.StreamId);
                responseSent.TrySetResult();
            }

            return Task.CompletedTask;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (!predicate())
        {
            await Task.Delay(10, cts.Token).ConfigureAwait(false);
        }
    }

    private sealed class CanceledResponseDispatcher : IServiceDispatcher
    {
        private readonly TaskCompletionSource _producerStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ServiceName => "CanceledResponse";

        public Task ProducerStarted => _producerStarted.Task;

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            IRpcStreamingContext streaming,
            CancellationToken ct = default)
        {
            streaming.SetResponse(ItemsAsync());
            return Task.CompletedTask;
        }

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        private async IAsyncEnumerable<int> ItemsAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            _producerStarted.TrySetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            yield return 1;
        }
    }
}
