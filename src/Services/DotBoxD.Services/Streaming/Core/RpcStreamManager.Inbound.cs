using DotBoxD.Services.Buffers;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Frames;

namespace DotBoxD.Services.Streaming.Core;

internal sealed partial class RpcStreamManager
{
    public RpcStreamReceiver GetRegisteredInbound(RpcStreamHandle handle)
    {
        if (handle.StreamId == 0)
        {
            throw new ServiceProtocolException("Stream id must not be zero.");
        }

        RpcStreamValidation.ValidateKind(handle.Kind);
        if (!_receivers.TryGetValue(handle.StreamId, out var existing))
        {
            _canceledInbound.ThrowIfOverflowed();
            throw new ServiceProtocolException($"Inbound stream id '{handle.StreamId}' was not registered.");
        }

        if (existing.Handle.Kind != handle.Kind)
        {
            throw new ServiceProtocolException(
                $"Inbound stream id '{handle.StreamId}' is '{existing.Handle.Kind}', not '{handle.Kind}'.");
        }

        return existing;
    }

    public RpcStreamReceiver RegisterInboundResponse(RpcStreamHandle handle, CancellationToken ct) =>
        RegisterInbound(handle, ct);

    public void RegisterInbound(RpcStreamHandle[]? handles, CancellationToken ct)
    {
        if (handles is null)
        {
            return;
        }

        if (handles.Length == 1)
        {
            RegisterInbound(handles[0], ct);
            return;
        }

        EnsureCanRegisterInbound(handles.Length);
        RegisterInboundMany(handles, ct);
    }

    private void RegisterInboundMany(RpcStreamHandle[] handles, CancellationToken ct)
    {
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
            RollBackInboundRegistration(registered);
            throw;
        }
    }

    private void RollBackInboundRegistration(List<int> registered)
    {
        foreach (var streamId in registered)
        {
            if (!_receivers.TryGetValue(streamId, out var receiver))
            {
                continue;
            }

            try
            {
                RemoveCanceledInbound(streamId);
            }
            catch (Exception ex)
            {
                RpcDiagnostics.Report("Rolled back inbound stream tracking failed", ex);
            }

            receiver.Abort(new ServiceProtocolException("Inbound stream registration failed."));
        }
    }

    private RpcStreamReceiver RegisterInbound(RpcStreamHandle handle, CancellationToken ct)
    {
        if (handle.StreamId == 0)
        {
            throw new ServiceProtocolException("Stream id must not be zero.");
        }

        RpcStreamValidation.ValidateKind(handle.Kind);
        RpcStreamReceiver receiver;
        lock (_inboundGate)
        {
            _canceledInbound.ThrowIfOverflowed();
            EnsureInboundCapacityLocked(1);
            if (_canceledInbound.Contains(handle.StreamId))
            {
                throw new ServiceProtocolException(
                    $"Inbound stream id '{handle.StreamId}' is awaiting a terminal frame after local cancellation.");
            }

            receiver = new RpcStreamReceiver(this, handle);
            if (!_receivers.TryAdd(handle.StreamId, receiver) &&
                (!_receivers.TryGetValue(handle.StreamId, out var existing) ||
                 !existing.IsCompleted ||
                 !_receivers.TryUpdate(handle.StreamId, receiver, existing)))
            {
                throw new ServiceProtocolException($"Inbound stream id '{handle.StreamId}' is already active.");
            }

            Interlocked.Increment(ref _activeInboundCount);
        }

        receiver.SendCreditBestEffort(WindowSize, ct);
        return receiver;
    }

    private void EnsureCanRegisterInbound(int count)
    {
        lock (_inboundGate)
        {
            _canceledInbound.ThrowIfOverflowed();
            EnsureInboundCapacityLocked(count);
        }
    }

    private void EnsureInboundCapacityLocked(int count)
    {
        var active = Volatile.Read(ref _activeInboundCount);
        var available = _maxInboundStreamsPerPeer - active;
        if (count <= available)
        {
            return;
        }

        throw new ServiceProtocolException(
            $"Inbound stream capacity exceeded: {count} requested, {Math.Max(available, 0)} available, maximum active inbound streams per peer is {_maxInboundStreamsPerPeer}.");
    }

    public bool TryAcceptItem(int streamId, Payload frame)
    {
        if (!_receivers.TryGetValue(streamId, out var receiver))
        {
            return _canceledInbound.TryConsumeItem(streamId, frame);
        }

        AfterInboundReceiverObservedForTest?.Invoke(streamId, receiver);
        var result = receiver.TryAccept(frame);
        return result is RpcStreamAcceptResult.Accepted or RpcStreamAcceptResult.Consumed or RpcStreamAcceptResult.Rejected ||
            _canceledInbound.TryConsumeItem(streamId, frame);
    }

    public bool TryCompleteInbound(int streamId) => TryCompleteInbound(streamId, error: null);

    public void CompleteInbound(int streamId) => _ = TryCompleteInbound(streamId);

    public void CompleteInbound(int streamId, Exception? error) =>
        _ = TryCompleteInbound(streamId, error);

    private bool TryCompleteInbound(int streamId, Exception? error)
    {
        lock (_inboundGate)
        {
            if (_receivers.TryGetValue(streamId, out var receiver))
            {
                receiver.Complete(error);
                return true;
            }

            return _canceledInbound.TryRemove(streamId);
        }
    }

    public bool TryCompleteInboundError(Payload frame) =>
        TryCompleteInboundError(frame.Memory);

    public bool TryCompleteInboundError(ReadOnlyMemory<byte> frame) =>
        TryCompleteInboundError(frame, out _);

    public bool TryCompleteInboundError(ReadOnlyMemory<byte> frame, out bool malformed)
    {
        malformed = false;
        if (!RpcStreamErrorFrameReader.TryRead(frame, _serializer, out var streamId, out var response))
        {
            malformed = true;
            return false;
        }

        return TryCompleteInbound(
            streamId,
            new RemoteServiceException(
                response.ErrorMessage ?? "Remote stream failed.",
                response.ErrorType ?? "Unknown"));
    }

    public void RemoveInbound(int streamId) => AbortInbound(streamId);

    internal void RemoveCompletedInbound(RpcStreamReceiver receiver)
    {
        lock (_inboundGate)
        {
            var streamId = receiver.Handle.StreamId;
            if (_receivers.TryGetValue(streamId, out var current) &&
                ReferenceEquals(current, receiver))
            {
                _receivers.TryRemove(streamId, out _);
            }
        }
    }

    internal void RemoveCanceledInbound(int streamId)
    {
        lock (_inboundGate)
        {
            try
            {
                _canceledInbound.Add(streamId);
            }
            finally
            {
                _receivers.TryRemove(streamId, out _);
            }
        }
    }

    private void AbortInbound(int streamId)
    {
        if (_receivers.TryRemove(streamId, out var receiver))
        {
            receiver.Abort(new ServiceConnectionException($"Stream '{streamId}' is no longer active."));
        }
    }
}
