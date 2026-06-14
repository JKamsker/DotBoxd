using DotBoxd.Services;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Streaming;
using DotBoxd.Codecs.MessagePack;
using Xunit;

namespace DotBoxd.Services.Tests;

public sealed class StreamingCompleteAcceptRaceTests
{
    [Fact]
    public void TryAcceptItem_WhenCompleteInboundRacesBetweenLookupAndAccept_SilentlyConsumesItem()
    {
        var streams = CreateStreamManager();
        var handle = new RpcStreamHandle(42, RpcStreamKind.Binary);
        streams.RegisterInboundResponse(handle, CancellationToken.None);

        streams.AfterInboundReceiverObservedForTest = (streamId, _) =>
        {
            streams.CompleteInbound(streamId);
        };

        using var frame = MessageFramer.FrameToPayload(
            42,
            MessageType.StreamItem,
            new byte[] { 1, 2, 3 });

        var accepted = streams.TryAcceptItem(42, frame);

        Assert.True(
            accepted,
            "A StreamItem that races with StreamComplete should be silently consumed, not reported as a protocol error.");
    }

    private static RpcStreamManager CreateStreamManager()
    {
        var serializer = new MessagePackRpcSerializer();
        return new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
