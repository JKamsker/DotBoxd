using DotBoxD.Services.Buffers;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;

namespace DotBoxD.Services.Streaming.Core;

internal sealed partial class RpcStreamManager
{
    public async Task SendStreamItemAsync(int streamId, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var state = GetSender(streamId);
        await state.WaitForCreditAsync(ct).ConfigureAwait(false);
        var frame = RpcRawFrame.RentFrame(streamId, MessageType.StreamItem, payload.Span);
        await _frameSender.SendAsync(frame, ct).ConfigureAwait(false);
    }

    public async Task SendStreamItemAsync<T>(
        int streamId,
        T item,
        ISerializer serializer,
        CancellationToken ct)
    {
        var state = GetSender(streamId);
        await state.WaitForCreditAsync(ct).ConfigureAwait(false);
        var frame = PooledBufferWriter.Rent(MessageFramer.HeaderSize);
        try
        {
            RpcRawFrame.WritePrefix(frame, streamId, MessageType.StreamItem);
            serializer.Serialize(frame, item);
            RpcRawFrame.Complete(frame);
            await _frameSender.SendAsync(frame, ct).ConfigureAwait(false);
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    public Task SendStreamCompleteAsync(int streamId, CancellationToken ct) =>
        SendControlAsync(streamId, MessageType.StreamComplete, ct);

    public Task SendCancelAsync(int streamId, CancellationToken ct) =>
        SendControlAsync(streamId, MessageType.StreamCancel, ct);

    public async Task SendCreditAsync(int streamId, int count, CancellationToken ct)
    {
        var frame = RpcRawFrame.RentInt32Frame(streamId, MessageType.StreamCredit, count);
        await _frameSender.SendAsync(frame, ct).ConfigureAwait(false);
    }

    public async Task SendStreamErrorAsync(int streamId, Exception error, CancellationToken ct)
    {
        var rpcError = RpcErrors.FromException(error, _exceptionTransformer);
        var frame = MessageFramer.RentFrameMessage(
            _serializer,
            streamId,
            MessageType.StreamError,
            new RpcResponse
            {
                MessageId = streamId,
                IsSuccess = false,
                ErrorMessage = rpcError.Message,
                ErrorType = rpcError.Type,
            },
            ReadOnlySpan<byte>.Empty);
        await _frameSender.SendAsync(frame, ct).ConfigureAwait(false);
    }

    private async Task SendControlAsync(int streamId, MessageType type, CancellationToken ct)
    {
        var frame = RpcRawFrame.RentFrame(streamId, type, ReadOnlySpan<byte>.Empty);
        await _frameSender.SendAsync(frame, ct).ConfigureAwait(false);
    }
}
