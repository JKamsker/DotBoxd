using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class StreamingCreditWindowRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ActiveStreamCredits_CannotExceedReceiverWindow()
    {
        var streams = CreateStreamManager();
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        await using var outbound = RegisterOutbound(streams, handle);
        using var windowCredit = CreditFrame(handle.StreamId, RpcStreamManager.WindowSize);
        using var extraCredit = CreditFrame(handle.StreamId, 1);

        Assert.True(streams.TryAddCredit(windowCredit));
        Assert.False(streams.TryAddCredit(extraCredit));

        await AssertCanSendWindowOnly(streams, handle.StreamId);
    }

    [Fact]
    public async Task ReservedStreamCredits_CannotAccumulateBeyondReceiverWindow()
    {
        var streams = CreateStreamManager();
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        using var windowCredit = CreditFrame(handle.StreamId, RpcStreamManager.WindowSize);
        using var extraCredit = CreditFrame(handle.StreamId, 1);

        Assert.True(streams.TryAddCredit(windowCredit));
        Assert.False(streams.TryAddCredit(extraCredit));
        Assert.Equal(1, streams.PendingCreditCount);

        await using var outbound = RegisterOutbound(streams, handle);
        await AssertCanSendWindowOnly(streams, handle.StreamId);
    }

    private static async Task AssertCanSendWindowOnly(RpcStreamManager streams, int streamId)
    {
        for (var i = 0; i < RpcStreamManager.WindowSize; i++)
        {
            await streams.SendStreamItemAsync(streamId, new byte[] { 1 }, CancellationToken.None)
                .WaitAsync(Timeout);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            streams.SendStreamItemAsync(streamId, new byte[] { 1 }, cts.Token));
    }

    private static RpcOutboundStreamSet RegisterOutbound(RpcStreamManager streams, RpcStreamHandle handle)
    {
        var attachment = RpcStreamAttachment.FromStream(handle, new MemoryStream());
        return streams.RegisterOutbound(new[] { attachment }, CancellationToken.None);
    }

    private static Payload CreditFrame(int streamId, int count) =>
        RpcRawFrame.FrameInt32(streamId, MessageType.StreamCredit, count);

    private static RpcStreamManager CreateStreamManager()
    {
        var serializer = new MessagePackRpcSerializer();
        return new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
