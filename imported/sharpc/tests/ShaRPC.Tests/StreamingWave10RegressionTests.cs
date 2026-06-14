using ShaRPC.Core;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class StreamingWave10RegressionTests
{
    [Fact]
    public async Task ManyLocallyCanceledInboundStreams_WithoutRemoteTerminal_AreBoundedByCapacity()
    {
        var streams = CreateStreamManager();

        for (var i = 0; i < RpcCanceledInboundStreams.Capacity; i++)
        {
            var receiver = streams.RegisterInboundResponse(
                new RpcStreamHandle(10_000 + i, RpcStreamKind.Binary),
                CancellationToken.None);
            await receiver.CancelAsync();
        }

        Assert.Equal(0, streams.InboundReceiverCount);
        Assert.Equal(RpcCanceledInboundStreams.Capacity, streams.CanceledInboundCount);
    }

    [Fact]
    public async Task StreamItemRacingLocalCancel_IsConsumedWithoutProtocolError()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var streams = CreateStreamManager(serializer);
        var processor = CreateProcessor(serializer, streams, protocolErrors);
        var handle = new RpcStreamHandle(10_200, RpcStreamKind.Binary);
        streams.RegisterInboundResponse(handle, CancellationToken.None);
        var canceled = 0;
        streams.AfterInboundReceiverObservedForTest = (_, receiver) =>
        {
            if (Interlocked.Exchange(ref canceled, 1) == 0)
            {
                receiver.Cancel();
            }
        };
        var lateItem = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamItem,
            new byte[] { 1, 2, 3 });

        try
        {
            var shouldDispose = await processor.ShouldDisposeAsync(lateItem, CancellationToken.None);

            Assert.False(shouldDispose);
            Assert.Empty(protocolErrors);
            Assert.Throws<ObjectDisposedException>(() => _ = lateItem.Memory);
            Assert.Equal(0, streams.InboundReceiverCount);
            Assert.Equal(1, streams.CanceledInboundCount);
        }
        finally
        {
            lateItem.Dispose();
        }
    }

    [Fact]
    public async Task MalformedLateStreamErrorAfterLocalCancel_ReportsProtocolErrorAndKeepsTombstone()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var streams = CreateStreamManager(serializer);
        var processor = CreateProcessor(serializer, streams, protocolErrors);
        var handle = new RpcStreamHandle(10_300, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
        await receiver.CancelAsync();
        using var malformedError = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamError,
            ReadOnlySpan<byte>.Empty);

        var shouldDispose = await processor.ShouldDisposeAsync(malformedError, CancellationToken.None);

        Assert.True(shouldDispose);
        Assert.Single(protocolErrors, error => error.Contains("Malformed stream error frame."));
        Assert.Equal(1, streams.CanceledInboundCount);
    }

    [Fact]
    public async Task ValidLateStreamErrorAfterLocalCancel_IsSuppressedAndConsumesTombstone()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var streams = CreateStreamManager(serializer);
        var processor = CreateProcessor(serializer, streams, protocolErrors);
        var handle = new RpcStreamHandle(10_400, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
        await receiver.CancelAsync();
        using var lateError = MessageFramer.FrameMessage(
            serializer,
            handle.StreamId,
            MessageType.StreamError,
            new RpcResponse
            {
                MessageId = handle.StreamId,
                IsSuccess = false,
                ErrorMessage = "late failure",
                ErrorType = "Remote",
            },
            ReadOnlySpan<byte>.Empty);

        var shouldDispose = await processor.ShouldDisposeAsync(lateError, CancellationToken.None);

        Assert.True(shouldDispose);
        Assert.Empty(protocolErrors);
        Assert.Equal(0, streams.CanceledInboundCount);
    }

    private static RpcStreamManager CreateStreamManager() =>
        CreateStreamManager(new MessagePackRpcSerializer());

    private static RpcStreamManager CreateStreamManager(MessagePackRpcSerializer serializer) =>
        new(serializer, SendNoopAsync, exceptionTransformer: null);

    private static RpcPeerFrameProcessor CreateProcessor(
        MessagePackRpcSerializer serializer,
        RpcStreamManager streams,
        List<string> protocolErrors)
    {
        var inbound = new RpcPeerInboundDispatcher(
            serializer,
            new RpcPeerOptions(),
            streams,
            SendNoopAsync,
            (id, type, message, _) => protocolErrors.Add($"{id}:{type}:{message}"),
            dispatchError: static (_, _) => { });
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

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
