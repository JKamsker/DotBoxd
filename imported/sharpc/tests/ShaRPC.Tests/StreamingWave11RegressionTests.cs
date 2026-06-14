using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class StreamingWave11RegressionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RepeatedCancelAndValidTerminal_DoesNotGrowCanceledInboundTracking()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var streams = CreateStreamManager(serializer);
        var processor = CreateProcessor(serializer, streams, protocolErrors);
        var streamCount = RpcCanceledInboundStreams.Capacity + 64;

        for (var i = 0; i < streamCount; i++)
        {
            var handle = new RpcStreamHandle(11_000 + i, RpcStreamKind.Binary);
            var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
            await receiver.CancelAsync();

            using var complete = MessageFramer.FrameToPayload(
                handle.StreamId,
                MessageType.StreamComplete,
                ReadOnlySpan<byte>.Empty);
            Assert.True(await processor.ShouldDisposeAsync(complete, CancellationToken.None));
        }

        Assert.Empty(protocolErrors);
        Assert.Equal(0, streams.CanceledInboundCount);
        Assert.Equal(0, streams.CanceledInboundTrackingCount);
    }

    [Theory]
    [InlineData(MalformedStreamErrorKind.TrailingPayload)]
    [InlineData(MalformedStreamErrorKind.SuccessResponse)]
    [InlineData(MalformedStreamErrorKind.MismatchedMessageId)]
    [InlineData(MalformedStreamErrorKind.StreamResponse)]
    public async Task MalformedStreamErrorAfterLocalCancel_ReportsProtocolErrorAndKeepsTombstone(
        MalformedStreamErrorKind kind)
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var streams = CreateStreamManager(serializer);
        var processor = CreateProcessor(serializer, streams, protocolErrors);
        var handle = new RpcStreamHandle(12_000 + (int)kind, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
        await receiver.CancelAsync();
        using var error = CreateMalformedStreamError(serializer, handle, kind);

        Assert.True(await processor.ShouldDisposeAsync(error, CancellationToken.None));

        Assert.Single(protocolErrors, entry => entry.Contains("Malformed stream error frame."));
        Assert.Equal(1, streams.CanceledInboundCount);
        Assert.Equal(1, streams.CanceledInboundTrackingCount);
    }

    [Theory]
    [InlineData(MalformedStreamErrorKind.TrailingPayload)]
    [InlineData(MalformedStreamErrorKind.SuccessResponse)]
    [InlineData(MalformedStreamErrorKind.MismatchedMessageId)]
    [InlineData(MalformedStreamErrorKind.StreamResponse)]
    public async Task MalformedStreamErrorForActiveReceiver_DoesNotFaultReceiver(
        MalformedStreamErrorKind kind)
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var streams = CreateStreamManager(serializer);
        var processor = CreateProcessor(serializer, streams, protocolErrors);
        var handle = new RpcStreamHandle(13_000 + (int)kind, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
        using var error = CreateMalformedStreamError(serializer, handle, kind);

        Assert.True(await processor.ShouldDisposeAsync(error, CancellationToken.None));
        Assert.Single(protocolErrors, entry => entry.Contains("Malformed stream error frame."));
        Assert.Equal(1, streams.InboundReceiverCount);

        await AssertActiveReceiverAcceptsItemAsync(processor, receiver, handle.StreamId);
    }

    [Fact]
    public async Task MalformedStreamCompleteAfterLocalCancel_ReportsProtocolErrorAndKeepsTombstone()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var streams = CreateStreamManager(serializer);
        var processor = CreateProcessor(serializer, streams, protocolErrors);
        var handle = new RpcStreamHandle(14_000, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
        await receiver.CancelAsync();
        using var complete = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamComplete,
            new byte[] { 1 });

        Assert.True(await processor.ShouldDisposeAsync(complete, CancellationToken.None));

        Assert.Single(protocolErrors, entry => entry.Contains("Malformed stream complete frame."));
        Assert.Equal(1, streams.CanceledInboundCount);
        Assert.Equal(1, streams.CanceledInboundTrackingCount);
    }

    [Fact]
    public async Task MalformedStreamCompleteForActiveReceiver_DoesNotCompleteReceiver()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var streams = CreateStreamManager(serializer);
        var processor = CreateProcessor(serializer, streams, protocolErrors);
        var handle = new RpcStreamHandle(15_000, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
        using var complete = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamComplete,
            new byte[] { 1 });

        Assert.True(await processor.ShouldDisposeAsync(complete, CancellationToken.None));
        Assert.Single(protocolErrors, entry => entry.Contains("Malformed stream complete frame."));
        Assert.Equal(1, streams.InboundReceiverCount);

        await AssertActiveReceiverAcceptsItemAsync(processor, receiver, handle.StreamId);
    }

    private static async Task AssertActiveReceiverAcceptsItemAsync(
        RpcPeerFrameProcessor processor,
        RpcStreamReceiver receiver,
        int streamId)
    {
        var item = MessageFramer.FrameToPayload(
            streamId,
            MessageType.StreamItem,
            new byte[] { 2, 3, 5 });

        Assert.False(await processor.ShouldDisposeAsync(item, CancellationToken.None));
        using var chunk = await receiver.ReadChunkAsync(CancellationToken.None)
            .AsTask()
            .WaitAsync(TestTimeout);
        Assert.NotNull(chunk);
        Assert.Equal(new byte[] { 2, 3, 5 }, chunk.Payload.ToArray());
    }

    private static Payload CreateMalformedStreamError(
        MessagePackRpcSerializer serializer,
        RpcStreamHandle handle,
        MalformedStreamErrorKind kind)
    {
        var response = new RpcResponse
        {
            MessageId = kind == MalformedStreamErrorKind.MismatchedMessageId
                ? handle.StreamId + 1
                : handle.StreamId,
            IsSuccess = kind == MalformedStreamErrorKind.SuccessResponse,
            ErrorMessage = "remote failed",
            ErrorType = "Remote",
            Stream = kind == MalformedStreamErrorKind.StreamResponse
                ? new RpcStreamHandle(handle.StreamId + 10_000, RpcStreamKind.Binary)
                : null,
        };
        var payload = kind == MalformedStreamErrorKind.TrailingPayload
            ? new byte[] { 1 }
            : ReadOnlySpan<byte>.Empty;

        return MessageFramer.FrameMessage(
            serializer,
            handle.StreamId,
            MessageType.StreamError,
            response,
            payload);
    }

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

    public enum MalformedStreamErrorKind
    {
        TrailingPayload,
        SuccessResponse,
        MismatchedMessageId,
        StreamResponse,
    }
}
