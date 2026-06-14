using System.Buffers.Binary;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Streaming;
using DotBoxd.Codecs.MessagePack;
using Xunit;

namespace DotBoxd.Services.Tests.Coverage;

public sealed class Wave2_PerfAndCoverageTests
{
    // ────────────────────────────────────────────────────────────────────
    // COVERAGE: MessageFramer.TryReadFrame — negative envelopeLength
    // guard. The check at line 227 returns false when envelopeLength < 0,
    // but no test exercises this specific branch.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryReadFrame_NegativeEnvelopeLength_ReturnsFalse()
    {
        const int headerSize = MessageFramer.HeaderSize;
        const int envelopeLengthSize = MessageFramer.EnvelopeLengthSize;
        var totalLength = headerSize + envelopeLengthSize;
        var buffer = new byte[totalLength];

        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), totalLength);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), 42);
        buffer[8] = (byte)MessageType.Response;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(headerSize, envelopeLengthSize), -1);

        var result = MessageFramer.TryReadFrame(
            buffer,
            out _,
            out _,
            out _,
            out _);

        Assert.False(result, "TryReadFrame should reject a frame with negative envelopeLength.");
    }

    // ────────────────────────────────────────────────────────────────────
    // COVERAGE: MessageFramer.ReadMessageAsync — partial payload close.
    // When the connection closes mid-payload (0 < bytesRead < payloadLength),
    // an InvalidDataException with the partial-byte count should be thrown.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadMessageAsync_PartialPayloadClose_ThrowsWithByteCount()
    {
        const int headerSize = MessageFramer.HeaderSize;
        var payloadLength = 100;
        var totalLength = headerSize + payloadLength;

        var headerBytes = new byte[headerSize];
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(0, 4), totalLength);
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(4, 4), 1);
        headerBytes[8] = (byte)MessageType.Response;

        var partialPayload = new byte[30];
        var ms = new MemoryStream();
        ms.Write(headerBytes);
        ms.Write(partialPayload);
        ms.Position = 0;

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => MessageFramer.ReadMessageAsync(ms, CancellationToken.None));

        Assert.Contains("30", ex.Message);
        Assert.Contains(payloadLength.ToString(), ex.Message);
    }

    // ────────────────────────────────────────────────────────────────────
    // COVERAGE: RpcStreamSendState.DisposeAfterCompletion — does NOT
    // call Cancel(). The CTS should remain non-cancelled after disposal.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void DisposeAfterCompletion_DoesNotCancelCts()
    {
        var state = new RpcStreamSendState(1, CancellationToken.None);
        var tokenBefore = state.Token;
        Assert.False(tokenBefore.IsCancellationRequested);

        state.DisposeAfterCompletion();

        Assert.False(state.IsCancellationRequested,
            "DisposeAfterCompletion should not cancel the CTS; " +
            "normal stream completion should not trigger cancellation.");
    }

    // ────────────────────────────────────────────────────────────────────
    // COVERAGE: RpcStreamSendState.Dispose — DOES call Cancel() first.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_CancelsCtsBeforeDisposal()
    {
        var state = new RpcStreamSendState(1, CancellationToken.None);
        Assert.False(state.IsCancellationRequested);

        state.Dispose();

        Assert.True(state.IsCancellationRequested,
            "Dispose should cancel the CTS for abortive teardown.");
    }

    // ────────────────────────────────────────────────────────────────────
    // COVERAGE: RpcStreamSendState.AddCredit after Dispose — no-op.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddCredit_AfterDispose_IsNoOp()
    {
        var state = new RpcStreamSendState(1, CancellationToken.None);
        state.Dispose();

        var ex = Record.Exception(() => state.AddCredit(5));
        Assert.Null(ex);
    }

    // ────────────────────────────────────────────────────────────────────
    // COVERAGE: RpcStreamSendState.AddCredit with zero/negative — no-op.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddCredit_ZeroOrNegative_IsNoOp()
    {
        using var state = new RpcStreamSendState(1, CancellationToken.None);

        var ex = Record.Exception(() =>
        {
            state.AddCredit(0);
            state.AddCredit(-1);
        });

        Assert.Null(ex);
    }

    // ────────────────────────────────────────────────────────────────────
    // COVERAGE: RpcStreamSendState.Cancel after Dispose — no-op.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_AfterDispose_IsNoOp()
    {
        var state = new RpcStreamSendState(1, CancellationToken.None);
        state.Dispose();

        var ex = Record.Exception(() => state.Cancel());
        Assert.Null(ex);
    }

    // ────────────────────────────────────────────────────────────────────
    // COVERAGE: RpcStreamSendState double Dispose — idempotent.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_Twice_IsIdempotent()
    {
        var state = new RpcStreamSendState(1, CancellationToken.None);
        state.Dispose();

        var ex = Record.Exception(() => state.Dispose());
        Assert.Null(ex);
    }

    // ────────────────────────────────────────────────────────────────────
    // COVERAGE: RegisterInbound with single-handle array uses the fast
    // path (no List<int> allocation for rollback tracking).
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterInbound_SingleHandle_Succeeds()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null);

        var handles = new[] { new RpcStreamHandle(1, RpcStreamKind.Binary) };
        streams.RegisterInbound(handles, CancellationToken.None);

        Assert.Equal(1, streams.InboundReceiverCount);
        streams.Stop();
    }

    // ────────────────────────────────────────────────────────────────────
    // COVERAGE: RegisterInbound with multi-handle array.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterInbound_MultiHandle_RegistersAll()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null);

        var handles = new[]
        {
            new RpcStreamHandle(1, RpcStreamKind.Binary),
            new RpcStreamHandle(2, RpcStreamKind.Items),
            new RpcStreamHandle(3, RpcStreamKind.Binary),
        };
        streams.RegisterInbound(handles, CancellationToken.None);

        Assert.Equal(3, streams.InboundReceiverCount);
        streams.Stop();
    }

    // ────────────────────────────────────────────────────────────────────
    // COVERAGE: RegisterInbound batch rollback when second handle is
    // a duplicate of the first. The first receiver should be cleaned up.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterInbound_DuplicateInBatch_RollsBackFirst()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null);

        var handles = new[]
        {
            new RpcStreamHandle(1, RpcStreamKind.Binary),
            new RpcStreamHandle(1, RpcStreamKind.Binary),
        };

        Assert.Throws<DotBoxd.Services.Exceptions.DotBoxdRpcProtocolException>(
            () => streams.RegisterInbound(handles, CancellationToken.None));

        Assert.Equal(0, streams.InboundReceiverCount);
        streams.Stop();
    }

    // ────────────────────────────────────────────────────────────────────
    // COVERAGE: RegisterInbound with null handles is a no-op.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterInbound_NullHandles_IsNoOp()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null);

        streams.RegisterInbound(null, CancellationToken.None);
        Assert.Equal(0, streams.InboundReceiverCount);
        streams.Stop();
    }

    // ────────────────────────────────────────────────────────────────────
    // COVERAGE: RpcCanceledInboundStreams.ThrowIfOverflowed resets
    // _overflowed when count drops below capacity.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void CanceledInboundStreams_ThrowIfOverflowed_ResetsAfterRemoval()
    {
        var canceled = new RpcCanceledInboundStreams();

        for (var i = 0; i < RpcCanceledInboundStreams.Capacity; i++)
        {
            canceled.Add(i);
        }

        Assert.Throws<DotBoxd.Services.Exceptions.DotBoxdRpcProtocolException>(
            () => canceled.Add(RpcCanceledInboundStreams.Capacity));

        canceled.Remove(0);

        var ex = Record.Exception(() => canceled.ThrowIfOverflowed());
        Assert.Null(ex);
    }
}
