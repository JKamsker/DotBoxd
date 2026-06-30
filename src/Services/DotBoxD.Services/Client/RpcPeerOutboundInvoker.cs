using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;

namespace DotBoxD.Services.Client;

internal sealed partial class RpcPeerOutboundInvoker : IRpcInvoker
{
    private readonly ISerializer _serializer;
    private readonly TimeSpan _timeout;
    private readonly int _maxPendingRequests;
    private readonly bool _enableLowAllocationValueTaskInvocations;
    private readonly Action _ensureStarted;
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _sendAsync;
    private readonly Func<PooledBufferWriter, CancellationToken, ValueTask>? _sendFrameAsync;
    private readonly RpcStreamManager _streams;
    private readonly RpcPeerStreamingCalls _streamingCalls;
    private readonly PendingRequests _pending = new();
    private readonly RpcPeerCancelFrameSender _cancelFrames;
    private int _messageIdCounter;
    private int _pendingCount;

    public RpcPeerOutboundInvoker(
        ISerializer serializer,
        RpcPeerOptions options,
        Action ensureStarted,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        RpcStreamManager streams)
        : this(serializer, options, ensureStarted, sendAsync, sendFrameAsync: null, streams)
    {
    }

    public RpcPeerOutboundInvoker(
        ISerializer serializer,
        RpcPeerOptions options,
        Action ensureStarted,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        Func<PooledBufferWriter, CancellationToken, ValueTask>? sendFrameAsync,
        RpcStreamManager streams)
    {
        _serializer = serializer;
        _timeout = options.RequestTimeout;
        _maxPendingRequests = options.MaxPendingRequests;
        _enableLowAllocationValueTaskInvocations = options.EnableLowAllocationValueTaskInvocations;
        _ensureStarted = ensureStarted;
        _sendAsync = sendAsync;
        _sendFrameAsync = sendFrameAsync;
        _streams = streams;
        _streamingCalls = new RpcPeerStreamingCalls(serializer);
        _cancelFrames = new RpcPeerCancelFrameSender(sendAsync);
    }

    public RpcStreamHandle ReserveStream(RpcStreamKind kind) =>
        _streams.ReserveOutbound(kind);

    public void ReleaseStream(RpcStreamHandle handle) =>
        _streams.ReleaseOutboundReservation(handle.StreamId);

    public Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default) =>
        SendUnaryRequestAsync<TRequest, TResponse>(service, method, request, instanceId: null, ct);

    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[] streams,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId: null, streams, ct).ConfigureAwait(false);
        return DeserializeNonStreamingResponse<TResponse>(received);
    }

    public Task<TResponse> InvokeAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default) =>
        SendUnaryRequestAsync<TResponse>(service, method, instanceId: null, ct);

    public async Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId: null, streams: null, ct).ConfigureAwait(false);
        EnsureNoResponsePayload(received);
    }

    public async Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[] streams,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId: null, streams, ct).ConfigureAwait(false);
        EnsureNoResponsePayload(received);
    }

    public async Task InvokeAsync(string service, string method, CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId: null, ct).ConfigureAwait(false);
        EnsureNoResponsePayload(received);
    }

    public void FailPending(Exception error) => _pending.FailAll(error);

    public Task StopCancelFramesAsync()
    {
        _pending.Dispose();
        return _cancelFrames.StopAsync();
    }

    private Task<ReceivedResponse> SendRequestAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        string? instanceId,
        RpcStreamAttachment[]? streams,
        CancellationToken ct)
        => SendRequestAsync(
            service,
            method,
            request,
            instanceId,
            RpcStreamAttachmentSet.FromArray(streams),
            ct);

    private Task<ReceivedResponse> SendRequestAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        string? instanceId,
        RpcStreamAttachment stream,
        CancellationToken ct)
        => SendRequestAsync(
            service,
            method,
            request,
            instanceId,
            RpcStreamAttachmentSet.FromSingle(stream),
            ct);

    private Task<ReceivedResponse> SendRequestAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        string? instanceId,
        RpcStreamAttachmentSet streams,
        CancellationToken ct)
    {
        try
        {
            ValidateTarget(service, method);
            _ensureStarted();
        }
        catch (Exception ex)
        {
            _streams.ReleaseOutboundReservations(streams);
            return DisposeStreamSourcesAndThrowAsync(streams, ex);
        }

        PendingReceivedResponse pending;
        try
        {
            pending = ReservePendingRequest(ct);
        }
        catch (Exception ex)
        {
            _streams.ReleaseOutboundReservations(streams);
            return DisposeStreamSourcesAndThrowAsync(streams, ex);
        }

        var outboundStreams = RpcOutboundStreamSet.Empty;
        var registeredStreams = false;
        PooledBufferWriter frame;
        try
        {
            outboundStreams = _streams.RegisterOutbound(streams, ct);
            registeredStreams = true;
            var envelope = CreateEnvelope(pending.MessageId, service, method, instanceId, streams);
            frame = MessageFramer.RentFrameRequest(
                _serializer,
                pending.MessageId,
                MessageType.Request,
                envelope,
                request);
        }
        catch (Exception ex)
        {
            // Registration or frame construction threw before SendFrameAndAwaitAsync took
            // ownership of the reserved slot, so release it here; otherwise the admission gate
            // leaks one slot per local setup failure and eventually rejects every call.
            _pending.Remove(pending.MessageId, pending, consumed: true);
            ReleasePendingSlot();
            return CleanupOutboundSetupFailureAsync(outboundStreams, streams, registeredStreams, ex);
        }

        return SendFrameAndAwaitAsync(
            pending.MessageId,
            pending,
            frame,
            service,
            method,
            outboundStreams,
            ct);
    }

    private Task<ReceivedResponse> SendRequestAsync(
        string service,
        string method,
        string? instanceId,
        CancellationToken ct)
    {
        ValidateTarget(service, method);
        _ensureStarted();
        var pending = ReservePendingRequest(ct);
        try
        {
            var envelope = CreateEnvelope(pending.MessageId, service, method, instanceId, streams: null);
            var frame = MessageFramer.RentFrameMessage(
                _serializer,
                pending.MessageId,
                MessageType.Request,
                envelope,
                ReadOnlySpan<byte>.Empty);
            return SendFrameAndAwaitAsync(
                pending.MessageId,
                pending,
                frame,
                service,
                method,
                RpcOutboundStreamSet.Empty,
                ct);
        }
        catch
        {
            // Frame construction (serialization) threw before SendFrameAndAwaitAsync took
            // ownership of the reserved slot, so release it here; otherwise the admission gate
            // leaks one slot per serialization failure and eventually rejects every call.
            _pending.Remove(pending.MessageId, pending, consumed: true);
            ReleasePendingSlot();
            throw;
        }
    }

}
