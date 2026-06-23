using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Frames;

namespace DotBoxD.Services.Streaming.Core;

internal sealed partial class RpcStreamManager
{
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
            throw new ServiceProtocolException("Stream id must not be zero.");
        }

        if (_senders.ContainsKey(streamId) || !_reservedOutbound.TryAdd(streamId, 0))
        {
            throw new ServiceProtocolException($"Duplicate outbound stream id '{streamId}'.");
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

    public RpcOutboundStreamSet RegisterOutbound(RpcStreamAttachment[]? attachments, CancellationToken ct)
    {
        if (attachments is null || attachments.Length == 0)
        {
            return RpcOutboundStreamSet.Empty;
        }

        return attachments.Length == 1
            ? RegisterOutbound(attachments[0], ct)
            : RegisterOutboundMany(attachments, ct);
    }

    private RpcOutboundStreamSet RegisterOutboundMany(RpcStreamAttachment[] attachments, CancellationToken ct)
    {
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
                    throw new ServiceProtocolException($"Duplicate outbound stream id '{attachments[i].Handle.StreamId}'.");
                }

                added[addedCount++] = state;
                CompleteOutboundRegistration(state);
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
                if (attachment is not null)
                {
                    ReleaseOutboundReservation(attachment.Handle.StreamId);
                }
            }

            throw;
        }
    }

    public RpcOutboundStreamSet RegisterOutbound(RpcStreamAttachment attachment, CancellationToken ct)
    {
        RpcStreamValidation.ValidateOutboundAttachment(attachment);
        var rows = new (RpcStreamAttachment Attachment, RpcStreamSendState State)[1];
        var state = new RpcStreamSendState(attachment.Handle.StreamId, ct);
        var added = false;
        try
        {
            if (!_senders.TryAdd(state.StreamId, state))
            {
                throw new ServiceProtocolException($"Duplicate outbound stream id '{attachment.Handle.StreamId}'.");
            }

            added = true;
            CompleteOutboundRegistration(state);
            rows[0] = (attachment, state);
            return new RpcOutboundStreamSet(this, _serializer, rows);
        }
        catch
        {
            if (added)
            {
                RemoveOutbound(state.StreamId);
            }
            else
            {
                state.Dispose();
            }

            ReleaseOutboundReservation(attachment.Handle.StreamId);
            throw;
        }
    }

    public bool TryAddCredit(Payload frame) =>
        TryAddCredit(frame.Memory);

    public bool TryAddCredit(ReadOnlyMemory<byte> frame)
    {
        if (!MessageFramer.TryReadFrameHeader(frame, out var streamId, out _) ||
            !RpcRawFrame.TryReadInt32(frame, out var count) ||
            count <= 0)
        {
            return false;
        }

        if (_senders.TryGetValue(streamId, out var state))
        {
            state.AddCredit(count);
            return true;
        }

        AfterOutboundSenderMissForTest?.Invoke(streamId);
        if (!_reservedOutbound.ContainsKey(streamId))
        {
            if (_senders.TryGetValue(streamId, out state))
            {
                state.AddCredit(count);
            }

            return true;
        }

        BufferReservedOutboundCredit(streamId, count);
        return true;
    }

    public void CancelOutbound(int streamId)
    {
        if (_senders.TryGetValue(streamId, out var state))
        {
            state.Cancel();
            return;
        }

        AfterOutboundSenderMissForTest?.Invoke(streamId);
        if (!_reservedOutbound.ContainsKey(streamId))
        {
            if (_senders.TryGetValue(streamId, out state))
            {
                state.Cancel();
            }

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

    public void RemoveOutbound(int streamId)
    {
        ClearOutboundTracking(streamId);
        if (_senders.TryRemove(streamId, out var state))
        {
            state.Dispose();
        }
    }

    internal void RemoveCompletedOutbound(int streamId)
    {
        ClearOutboundTracking(streamId);
        if (_senders.TryRemove(streamId, out var state))
        {
            state.DisposeAfterCompletion();
        }
    }

    private RpcStreamSendState GetSender(int streamId) =>
        _senders.TryGetValue(streamId, out var state)
            ? state
            : throw new ServiceConnectionException($"Stream '{streamId}' is no longer active.");

    private void CompleteOutboundRegistration(RpcStreamSendState state)
    {
        DrainPendingOutbound(state);
        _reservedOutbound.TryRemove(state.StreamId, out _);
        DrainPendingOutbound(state);
    }

    private void DrainPendingOutbound(RpcStreamSendState state)
    {
        if (_canceledOutbound.TryRemove(state.StreamId, out _))
        {
            state.Cancel();
            throw new OperationCanceledException("Stream was canceled before registration.");
        }

        if (_pendingCredits.TryRemove(state.StreamId, out var credits))
        {
            state.AddCredit(credits);
        }
    }

    private void BufferReservedOutboundCredit(int streamId, int count)
    {
        AfterReservedOutboundCreditObservedForTest?.Invoke(streamId);
        while (true)
        {
            if (_pendingCredits.TryGetValue(streamId, out var current))
            {
                var next = current > int.MaxValue - count ? int.MaxValue : current + count;
                if (_pendingCredits.TryUpdate(streamId, next, current))
                {
                    break;
                }
            }
            else if (_pendingCredits.TryAdd(streamId, count))
            {
                break;
            }
        }

        if (_senders.TryGetValue(streamId, out var state) &&
            _pendingCredits.TryRemove(streamId, out var pending))
        {
            state.AddCredit(pending);
        }
        else if (!_reservedOutbound.ContainsKey(streamId))
        {
            _pendingCredits.TryRemove(streamId, out _);
        }
    }

    private void ClearOutboundTracking(int streamId)
    {
        _pendingCredits.TryRemove(streamId, out _);
        _reservedOutbound.TryRemove(streamId, out _);
        _canceledOutbound.TryRemove(streamId, out _);
    }
}
