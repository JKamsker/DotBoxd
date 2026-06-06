using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

/// <summary>
/// Regression: <see cref="RpcStreamManager.CompleteInbound(int)"/> completes the receiver's
/// channel writer but does NOT remove the entry from the internal <c>_receivers</c> dictionary.
///
/// Consequences:
/// 1. <see cref="RpcStreamManager.InboundReceiverCount"/> remains non-zero even after the remote
///    sends <c>StreamComplete</c>, making diagnostics misleading.
/// 2. Re-registering the same stream ID (e.g. after reconnect) throws
///    <see cref="ShaRpcProtocolException"/> ("Inbound stream id 'X' is already active.") because
///    <c>RegisterInbound</c> calls <c>_receivers.TryAdd</c>, which sees the stale completed entry.
///
/// Compare: <see cref="RpcStreamManager.RemoveInbound"/> → <c>AbortInbound</c> DOES call
/// <c>_receivers.TryRemove</c>; only <c>CompleteInbound</c> skips that step.
///
/// These tests are RED: they assert the expected post-fix behaviour and FAIL against the current
/// production code.
/// </summary>
public sealed class StreamingCompleteInboundCleanupTests
{
    // -------------------------------------------------------------------------
    // Test 1 — InboundReceiverCount drops to zero after CompleteInbound
    // -------------------------------------------------------------------------

    /// <summary>
    /// After <c>CompleteInbound</c> is called (remote sends StreamComplete), the receiver entry
    /// must be removed from the dictionary so that <see cref="RpcStreamManager.InboundReceiverCount"/>
    /// returns zero.
    ///
    /// CURRENTLY FAILS: CompleteInbound does not remove from <c>_receivers</c>, so the count
    /// stays at 1.
    /// </summary>
    [Fact]
    public void CompleteInbound_RemovesReceiverFromDictionary()
    {
        // Arrange
        var streams = CreateStreamManager();
        var handle = new RpcStreamHandle(50_001, RpcStreamKind.Binary);
        streams.RegisterInboundResponse(handle, CancellationToken.None);

        Assert.Equal(1, streams.InboundReceiverCount); // sanity

        // Act — simulate the remote sending StreamComplete
        streams.CompleteInbound(handle.StreamId);

        // Assert — FAILS: count stays 1 because CompleteInbound does not call TryRemove
        Assert.Equal(0, streams.InboundReceiverCount);
    }

    // -------------------------------------------------------------------------
    // Test 2 — Same stream ID can be re-registered after CompleteInbound
    // -------------------------------------------------------------------------

    /// <summary>
    /// After <c>CompleteInbound</c> removes the stale entry, a subsequent
    /// <c>RegisterInboundResponse</c> for the same stream ID must succeed without throwing.
    ///
    /// CURRENTLY FAILS: The stale entry blocks <c>_receivers.TryAdd</c>, causing
    /// <see cref="ShaRpcProtocolException"/> ("Inbound stream id '50002' is already active.").
    /// </summary>
    [Fact]
    public void CompleteInbound_AllowsReregistrationWithSameStreamId()
    {
        // Arrange
        var streams = CreateStreamManager();
        var handle = new RpcStreamHandle(50_002, RpcStreamKind.Binary);
        streams.RegisterInboundResponse(handle, CancellationToken.None);

        // Act — simulate StreamComplete arriving from the remote
        streams.CompleteInbound(handle.StreamId);

        // Assert — re-registration must not throw
        // CURRENTLY FAILS: throws ShaRpcProtocolException("Inbound stream id '50002' is already active.")
        var exception = Record.Exception(() =>
            streams.RegisterInboundResponse(handle, CancellationToken.None));

        Assert.Null(exception);
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
