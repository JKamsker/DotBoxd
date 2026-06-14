using ShaRPC.Core;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class StreamingWave12RegressionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ZeroIdStreamComplete_ReportsProtocolError()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var processor = CreateProcessor(
            serializer,
            CreateStreamManager(serializer),
            protocolErrors);
        using var complete = MessageFramer.FrameToPayload(
            0,
            MessageType.StreamComplete,
            ReadOnlySpan<byte>.Empty);

        Assert.True(await processor.ShouldDisposeAsync(complete, CancellationToken.None));

        Assert.Single(protocolErrors, error => error.Contains("Malformed stream complete frame."));
    }

    [Fact]
    public async Task ValidShapedZeroIdStreamError_ReportsProtocolError()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var processor = CreateProcessor(
            serializer,
            CreateStreamManager(serializer),
            protocolErrors);
        using var error = MessageFramer.FrameMessage(
            serializer,
            0,
            MessageType.StreamError,
            new RpcResponse
            {
                MessageId = 0,
                IsSuccess = false,
                ErrorMessage = "remote failed",
                ErrorType = "Remote",
            },
            ReadOnlySpan<byte>.Empty);

        Assert.True(await processor.ShouldDisposeAsync(error, CancellationToken.None));

        Assert.Single(protocolErrors, entry => entry.Contains("Malformed stream error frame."));
    }

    [Fact]
    public async Task CanceledInboundStreamId_CannotBeRegisteredUntilValidTerminalConsumesTombstone()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new List<string>();
        var streams = CreateStreamManager(serializer);
        var processor = CreateProcessor(serializer, streams, protocolErrors);
        var handle = new RpcStreamHandle(16_000, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
        await receiver.CancelAsync();

        var error = Assert.Throws<ShaRpcProtocolException>(() =>
            streams.RegisterInboundResponse(handle, CancellationToken.None));
        Assert.Contains("awaiting a terminal frame", error.Message);
        Assert.Equal(0, streams.InboundReceiverCount);
        Assert.Equal(1, streams.CanceledInboundCount);

        using (var complete = MessageFramer.FrameToPayload(
                   handle.StreamId,
                   MessageType.StreamComplete,
                   ReadOnlySpan<byte>.Empty))
        {
            Assert.True(await processor.ShouldDisposeAsync(complete, CancellationToken.None));
        }

        Assert.Empty(protocolErrors);
        Assert.Equal(0, streams.CanceledInboundCount);

        var replacement = streams.RegisterInboundResponse(handle, CancellationToken.None);
        using var item = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamItem,
            new byte[] { 1, 2, 3 });
        Assert.False(await processor.ShouldDisposeAsync(item, CancellationToken.None));

        using var chunk = await replacement.ReadChunkAsync(CancellationToken.None)
            .AsTask()
            .WaitAsync(TestTimeout);
        Assert.NotNull(chunk);
        Assert.Equal(new byte[] { 1, 2, 3 }, chunk.Payload.ToArray());
    }

    [Fact]
    public async Task TerminalBeforeCancel_DoesNotLeaveStaleTombstone()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = CreateStreamManager(serializer);
        var handle = new RpcStreamHandle(16_100, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);

        streams.CompleteInbound(handle.StreamId);
        await receiver.CancelAsync();

        Assert.Equal(0, streams.InboundReceiverCount);
        Assert.Equal(0, streams.CanceledInboundCount);

        var replacement = streams.RegisterInboundResponse(handle, CancellationToken.None);
        Assert.Equal(handle, replacement.Handle);
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
}
