using System.Collections.Concurrent;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;

namespace ShaRPC.Core.Streaming;

internal sealed class RpcStreamManager
{
    public const int WindowSize = 4;

    private readonly ConcurrentDictionary<int, RpcStreamReceiver> _receivers = new();
    private readonly ConcurrentDictionary<int, byte> _canceledOutbound = new();
    private readonly ConcurrentDictionary<int, int> _pendingCredits = new();
    private readonly ConcurrentDictionary<int, byte> _reservedOutbound = new();
    private readonly ConcurrentDictionary<int, RpcStreamSendState> _senders = new();
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _sendAsync;
    private readonly ISerializer _serializer;
    private readonly Func<Exception, RpcErrorInfo?>? _exceptionTransformer;
    private int _outboundStreamIdCounter;

    public RpcStreamManager(
        ISerializer serializer,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        Func<Exception, RpcErrorInfo?>? exceptionTransformer)
    {
        _serializer = serializer;
        _sendAsync = sendAsync;
        _exceptionTransformer = exceptionTransformer;
    }

    internal int InboundReceiverCount => _receivers.Count;

    internal int OutboundSenderCount => _senders.Count;

    internal int PendingCreditCount => _pendingCredits.Count;

    // Deterministic coverage for the reserved-to-released pending-credit race; null in production.
    internal Action<int>? AfterReservedOutboundCreditObservedForTest { get; set; }

    public RpcStreamReceiver GetOrRegisterInbound(RpcStreamHandle handle, CancellationToken ct)
    {
        if (handle.StreamId == 0)
        {
            throw new ShaRpcProtocolException("Stream id must not be zero.");
        }

        if (_receivers.TryGetValue(handle.StreamId, out var existing))
        {
            if (existing.Handle.Kind != handle.Kind)
            {
                throw new ShaRpcProtocolException(
                    $"Inbound stream id '{handle.StreamId}' is '{existing.Handle.Kind}', not '{handle.Kind}'.");
            }

            return existing;
        }

        return RegisterInbound(handle, ct);
    }

    public RpcStreamReceiver RegisterInboundResponse(RpcStreamHandle handle, CancellationToken ct) =>
        RegisterInbound(handle, ct);

    internal RpcStreamHandle ReserveOutbound(RpcStreamKind kind)
    {
        RpcStreamValidation.ValidateKind(kind);
        while (true)
        {
            var streamId = Interlocked.Increment(ref _outboundStreamIdCounter);
            if (streamId == 0 || _senders.ContainsKey(streamId))
            {
                continue;
            }

            if (_reservedOutbound.TryAdd(streamId, 0))
            {
                return new RpcStreamHandle(streamId, kind);
            }
        }
    }

    internal void ReserveOutbound(int streamId)
    {
        if (streamId == 0)
        {
            throw new ShaRpcProtocolException("Stream id must not be zero.");
        }

        if (_senders.ContainsKey(streamId) || !_reservedOutbound.TryAdd(streamId, 0))
        {
            throw new ShaRpcProtocolException($"Duplicate outbound stream id '{streamId}'.");
        }
    }

    internal void ReleaseOutboundReservation(int streamId)
    {
        if (_reservedOutbound.TryRemove(streamId, out _))
        {
            _pendingCredits.TryRemove(streamId, out _);
            _canceledOutbound.TryRemove(streamId, out _);
        }
    }

    internal void ReleaseOutboundReservations(RpcStreamAttachment[]? attachments)
    {
        if (attachments is null)
        {
            return;
        }

        foreach (var attachment in attachments)
        {
            if (attachment is not null)
            {
                ReleaseOutboundReservation(attachment.Handle.StreamId);
            }
        }
    }

    public void RegisterInbound(RpcStreamHandle[]? handles, CancellationToken ct)
    {
        if (handles is null)
        {
            return;
        }

        var registered = new List<int>(handles.Length);
        try
        {
            foreach (var handle in handles)
            {
                RegisterInbound(handle, ct);
                registered.Add(handle.StreamId);
            }
        }
        catch
        {
            foreach (var streamId in registered)
            {
                if (_receivers.TryRemove(streamId, out var receiver))
                {
                    receiver.Abort(new ShaRpcProtocolException("Inbound stream registration failed."));
                }
            }

            throw;
        }
    }

    private RpcStreamReceiver RegisterInbound(RpcStreamHandle handle, CancellationToken ct)
    {
        if (handle.StreamId == 0)
        {
            throw new ShaRpcProtocolException("Stream id must not be zero.");
        }

        var receiver = new RpcStreamReceiver(this, handle);
        if (!_receivers.TryAdd(handle.StreamId, receiver))
        {
            throw new ShaRpcProtocolException($"Inbound stream id '{handle.StreamId}' is already active.");
        }

        _ = SendCreditAsync(handle.StreamId, WindowSize, ct);
        return receiver;
    }

    public RpcOutboundStreamSet RegisterOutbound(
        RpcStreamAttachment[]? attachments,
        CancellationToken ct)
    {
        if (attachments is null || attachments.Length == 0)
        {
            return RpcOutboundStreamSet.Empty;
        }

        var rows = new (RpcStreamAttachment Attachment, RpcStreamSendState State)[attachments.Length];
        var added = new RpcStreamSendState[attachments.Length];
        var addedCount = 0;
        try
        {
            RpcStreamValidation.ValidateOutboundAttachments(attachments);
            for (var i = 0; i < attachments.Length; i++)
            {
                var state = new RpcStreamSendState(attachments[i].Handle.StreamId, ct);
                if (!_senders.TryAdd(state.StreamId, state))
                {
                    state.Dispose();
                    throw new ShaRpcProtocolException($"Duplicate outbound stream id '{attachments[i].Handle.StreamId}'.");
                }

                added[addedCount++] = state;
                if (_canceledOutbound.TryRemove(state.StreamId, out _))
                {
                    state.Cancel();
                    throw new OperationCanceledException("Stream was canceled before registration.");
                }

                if (_pendingCredits.TryRemove(state.StreamId, out var credits))
                {
                    state.AddCredit(credits);
                }

                _reservedOutbound.TryRemove(state.StreamId, out _);
                rows[i] = (attachments[i], state);
            }

            return new RpcOutboundStreamSet(this, _serializer, rows);
        }
        catch
        {
            for (var i = 0; i < addedCount; i++)
            {
                RemoveOutbound(added[i].StreamId);
            }

            foreach (var attachment in attachments)
            {
                if (attachment is null)
                {
                    continue;
                }

                ReleaseOutboundReservation(attachment.Handle.StreamId);
            }

            throw;
        }
    }

    public bool TryAcceptItem(int streamId, Payload frame)
    {
        if (!_receivers.TryGetValue(streamId, out var receiver))
        {
            return false;
        }

        return receiver.TryAccept(frame);
    }

    public void CompleteInbound(int streamId) =>
        CompleteInbound(streamId, error: null);

    public void CompleteInbound(int streamId, Exception? error)
    {
        if (_receivers.TryGetValue(streamId, out var receiver))
        {
            receiver.Complete(error);
        }
    }

    public bool TryCompleteInboundError(Payload frame)
    {
        if (!MessageFramer.TryReadFrame(frame.Memory, out var streamId, out _, out var envelope, out _))
        {
            return false;
        }

        RpcResponse response;
        try
        {
            response = _serializer.Deserialize<RpcResponse>(envelope);
        }
        catch
        {
            return false;
        }

        CompleteInbound(
            streamId,
            new ShaRpcRemoteException(
                response.ErrorMessage ?? "Remote stream failed.",
                response.ErrorType ?? "Unknown"));
        return true;
    }

    public bool TryAddCredit(Payload frame)
    {
        if (!MessageFramer.TryReadFrameHeader(frame.Memory, out var streamId, out _) ||
            !RpcRawFrame.TryReadInt32(frame.Memory, out var count) ||
            count <= 0)
        {
            return false;
        }

        if (_senders.TryGetValue(streamId, out var state))
        {
            state.AddCredit(count);
            return true;
        }

        if (!_reservedOutbound.ContainsKey(streamId))
        {
            return true;
        }

        AfterReservedOutboundCreditObservedForTest?.Invoke(streamId);
        _pendingCredits.AddOrUpdate(
            streamId,
            count,
            (_, current) => current > int.MaxValue - count ? int.MaxValue : current + count);
        if (_senders.TryGetValue(streamId, out state) &&
            _pendingCredits.TryRemove(streamId, out var pending))
        {
            state.AddCredit(pending);
        }
        else if (!_reservedOutbound.ContainsKey(streamId))
        {
            _pendingCredits.TryRemove(streamId, out _);
        }

        return true;
    }

    public void CancelOutbound(int streamId)
    {
        if (_senders.TryGetValue(streamId, out var state))
        {
            state.Cancel();
            return;
        }

        if (!_reservedOutbound.ContainsKey(streamId))
        {
            return;
        }

        _canceledOutbound.TryAdd(streamId, 0);
        if (_senders.TryGetValue(streamId, out state) &&
            _canceledOutbound.TryRemove(streamId, out _))
        {
            state.Cancel();
        }
        else if (!_reservedOutbound.ContainsKey(streamId))
        {
            _canceledOutbound.TryRemove(streamId, out _);
        }
    }

    public void RemoveInbound(int streamId) =>
        AbortInbound(streamId);

    internal void RemoveCompletedInbound(int streamId) =>
        _receivers.TryRemove(streamId, out _);

    public void RemoveOutbound(int streamId)
    {
        _pendingCredits.TryRemove(streamId, out _);
        _reservedOutbound.TryRemove(streamId, out _);
        _canceledOutbound.TryRemove(streamId, out _);
        if (_senders.TryRemove(streamId, out var state))
        {
            state.Dispose();
        }
    }

    public async Task SendStreamItemAsync(int streamId, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var state = GetSender(streamId);
        await state.WaitForCreditAsync(ct).ConfigureAwait(false);
        using var frame = MessageFramer.FrameToPayload(streamId, MessageType.StreamItem, payload.Span);
        await _sendAsync(frame.Memory, ct).ConfigureAwait(false);
    }

    public async Task SendStreamItemAsync<T>(
        int streamId,
        T item,
        ISerializer serializer,
        CancellationToken ct)
    {
        var state = GetSender(streamId);
        await state.WaitForCreditAsync(ct).ConfigureAwait(false);
        using var writer = new PooledBufferWriter(MessageFramer.HeaderSize);
        RpcRawFrame.WritePrefix(writer, streamId, MessageType.StreamItem);
        serializer.Serialize(writer, item);
        using var frame = RpcRawFrame.Finish(writer);
        await _sendAsync(frame.Memory, ct).ConfigureAwait(false);
    }

    public Task SendStreamCompleteAsync(int streamId, CancellationToken ct) =>
        SendControlAsync(streamId, MessageType.StreamComplete, ct);

    public Task SendCancelAsync(int streamId, CancellationToken ct) =>
        SendControlAsync(streamId, MessageType.StreamCancel, ct);

    public async Task SendCreditAsync(int streamId, int count, CancellationToken ct)
    {
        using var frame = RpcRawFrame.FrameInt32(streamId, MessageType.StreamCredit, count);
        await _sendAsync(frame.Memory, ct).ConfigureAwait(false);
    }

    public async Task SendStreamErrorAsync(int streamId, Exception error, CancellationToken ct)
    {
        var rpcError = RpcErrors.FromException(error, _exceptionTransformer);
        using var frame = MessageFramer.FrameMessage(
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
        await _sendAsync(frame.Memory, ct).ConfigureAwait(false);
    }

    public void Stop()
    {
        foreach (var receiver in _receivers.Values)
        {
            receiver.Complete(new ShaRpcConnectionException("Connection closed."));
        }

        foreach (var sender in _senders.Values)
        {
            sender.Cancel();
        }
    }

    private RpcStreamSendState GetSender(int streamId) =>
        _senders.TryGetValue(streamId, out var state)
            ? state
            : throw new ShaRpcConnectionException($"Stream '{streamId}' is no longer active.");

    private void AbortInbound(int streamId)
    {
        if (_receivers.TryRemove(streamId, out var receiver))
        {
            receiver.Abort(new ShaRpcConnectionException($"Stream '{streamId}' is no longer active."));
        }
    }

    private async Task SendControlAsync(int streamId, MessageType type, CancellationToken ct)
    {
        using var frame = MessageFramer.FrameToPayload(streamId, type, ReadOnlySpan<byte>.Empty);
        await _sendAsync(frame.Memory, ct).ConfigureAwait(false);
    }
}
