using DotBoxd.Services.Protocol;
using DotBoxd.Services.Streaming;
using DotBoxd.Codecs.MessagePack;
using Xunit;

namespace DotBoxd.Services.Tests;

/// <summary>
/// Regression tests for <see cref="RpcStreamManager.Stop"/> leaving stale entries in
/// <c>_receivers</c> and <c>_senders</c>.
///
/// <see cref="RpcStreamManager.Stop"/> aborts all receivers and cancels all senders but does NOT
/// remove the entries from their respective dictionaries. As a result
/// <see cref="RpcStreamManager.InboundReceiverCount"/> and
/// <see cref="RpcStreamManager.OutboundSenderCount"/> remain non-zero after a stop.
///
/// Compare: <c>RemoveCompletedInbound</c> and <c>RemoveOutbound</c> both call
/// <c>TryRemove</c> on the relevant dictionary — only <c>Stop</c> skips that step.
/// </summary>
public sealed class StreamingStopCleanupTests
{
    // -------------------------------------------------------------------------
    // Test 1 — InboundReceiverCount drops to zero after Stop
    // -------------------------------------------------------------------------

    [Fact]
    public void Stop_Clears_InboundReceiverCount()
    {
        // Arrange
        var streams = CreateStreamManager();
        var handle1 = new RpcStreamHandle(41_001, RpcStreamKind.Binary);
        var handle2 = new RpcStreamHandle(41_002, RpcStreamKind.Binary);

        streams.RegisterInboundResponse(handle1, CancellationToken.None);
        streams.RegisterInboundResponse(handle2, CancellationToken.None);

        Assert.Equal(2, streams.InboundReceiverCount);

        // Act
        streams.Stop();

        // Assert — FAILS because Stop does not clear _receivers
        Assert.Equal(0, streams.InboundReceiverCount);
    }

    // -------------------------------------------------------------------------
    // Test 2 — OutboundSenderCount drops to zero after Stop
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Stop_Clears_OutboundSenderCount()
    {
        // Arrange
        var streams = CreateStreamManager();

        var handle1 = streams.ReserveOutbound(RpcStreamKind.Binary);
        var handle2 = streams.ReserveOutbound(RpcStreamKind.Binary);

        var attachments = new[]
        {
            RpcStreamAttachment.FromStream(handle1, new MemoryStream()),
            RpcStreamAttachment.FromStream(handle2, new MemoryStream()),
        };

        // RegisterOutbound moves both stream ids from _reservedOutbound into _senders.
        // We do NOT call outbound.Start() — we only need the senders to be present.
        await using var outbound = streams.RegisterOutbound(attachments, CancellationToken.None);

        Assert.Equal(2, streams.OutboundSenderCount);

        // Act
        streams.Stop();

        // Assert — FAILS because Stop does not clear _senders
        Assert.Equal(0, streams.OutboundSenderCount);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static RpcStreamManager CreateStreamManager()
    {
        var serializer = new MessagePackRpcSerializer();
        return new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
