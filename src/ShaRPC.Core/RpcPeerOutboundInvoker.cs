using ShaRPC.Core.Buffers;
using ShaRPC.Core.Client;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Streaming;
using ShaRPC.Core.Transport;
using System.IO.Pipelines;

namespace ShaRPC.Core;

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
    private readonly ShaRpcPendingRequests _pending = new();
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
        EnsureNonStreamingResponse(received);
    }

    public async Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[] streams,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId: null, streams, ct).ConfigureAwait(false);
        EnsureNonStreamingResponse(received);
    }

    public async Task InvokeAsync(string service, string method, CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId: null, ct).ConfigureAwait(false);
        EnsureNonStreamingResponse(received);
    }

    public Task<Stream> InvokeStreamAsync(
        string service,
        string method,
        CancellationToken ct = default) =>
        _streamingCalls.ReadStreamAsync(SendRequestAsync(service, method, instanceId: null, ct));

    public Task<Stream> InvokeStreamAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        _streamingCalls.ReadStreamAsync(SendRequestAsync(service, method, request, instanceId: null, streams, ct));

    public Task<Pipe> InvokePipeAsync(
        string service,
        string method,
        CancellationToken ct = default) =>
        _streamingCalls.ReadPipeAsync(SendRequestAsync(service, method, instanceId: null, ct));

    public Task<Pipe> InvokePipeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        _streamingCalls.ReadPipeAsync(SendRequestAsync(service, method, request, instanceId: null, streams, ct));

    public IAsyncEnumerable<T> InvokeAsyncEnumerable<T>(
        string service,
        string method,
        CancellationToken ct = default) =>
        _streamingCalls.EnumerateAsync<T>(
            invokeCt => SendRequestAsync(service, method, instanceId: null, invokeCt),
            ct);

    public IAsyncEnumerable<T> InvokeAsyncEnumerable<TRequest, T>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        _streamingCalls.EnumerateAsync<T>(
            invokeCt => SendRequestAsync(service, method, request, instanceId: null, streams, invokeCt),
            ct);

    public Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableAsync<T>(
        string service,
        string method,
        CancellationToken ct = default) =>
        _streamingCalls.ReadAsyncEnumerableAsync<T>(SendRequestAsync(service, method, instanceId: null, ct));

    public Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableAsync<TRequest, T>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        _streamingCalls.ReadAsyncEnumerableAsync<T>(
            SendRequestAsync(service, method, request, instanceId: null, streams, ct));

    public bool TryCompleteResponse(int messageId, RpcFrame frame)
    {
        if (!MessageFramer.TryReadFrame(frame.Memory, out _, out var messageType, out var envelope, out var payload))
        {
            _pending.TryFail(
                messageId,
                new ShaRpcProtocolException("Malformed response frame."));
            return false;
        }

        RpcResponse response;
        try
        {
            response = _serializer.Deserialize<RpcResponse>(envelope);
        }
        catch
        {
            _pending.TryFail(
                messageId,
                new ShaRpcProtocolException("Malformed response envelope."));
            return false;
        }

        if (messageType == MessageType.Error && response.IsSuccess)
        {
            _pending.TryFail(
                messageId,
                new ShaRpcProtocolException("Malformed error response frame."));
            return false;
        }

        if (!_pending.TryTake(messageId, out var completion))
        {
            return false;
        }

        RpcStreamReceiver? stream = null;
        try
        {
            if (response.Stream is { } handle &&
                completion.RegistersStreamingResponse)
            {
                stream = _streams.RegisterInboundResponse(handle, CancellationToken.None);
            }

            return completion.TrySetResponse(response, payload, frame, stream, _serializer);
        }
        catch (Exception ex)
        {
            stream?.Cancel();
            completion.SetError(ex);
            return false;
        }
    }

    public bool TryCompleteResponse(int messageId, Payload frame) =>
        TryCompleteResponse(messageId, new RpcFrame(frame));

    public void FailPending(Exception error) => _pending.FailAll(error);

    public Task StopCancelFramesAsync()
    {
        _pending.Dispose();
        return _cancelFrames.StopAsync();
    }

    private TResponse DeserializeNonStreamingResponse<TResponse>(ReceivedResponse received)
    {
        EnsureNonStreamingResponse(received);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    private static void EnsureNonStreamingResponse(ReceivedResponse received)
    {
        if (received.Response.Stream is not null)
        {
            throw new ShaRpcProtocolException(
                "Response opened a stream for a non-streaming invocation.");
        }
    }

    private Task<ReceivedResponse> SendRequestAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        string? instanceId,
        RpcStreamAttachment[]? streams,
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

    private async Task<ReceivedResponse> SendFrameAndAwaitAsync(
        int messageId,
        PendingReceivedResponse pending,
        PooledBufferWriter frame,
        string service,
        string method,
        RpcOutboundStreamSet outboundStreams,
        CancellationToken ct)
    {
        var consumed = false;
        var requestSent = false;
        try
        {
            using (frame)
            {
                await _sendAsync(frame.WrittenMemory, ct).ConfigureAwait(false);
                requestSent = true;
            }
            outboundStreams.Start();

            ReceivedResponse received;
            // Cancel through the pending-request table rather than the TCS directly, so the timeout and
            // an incoming response race on a single atomic removal: whichever removes the entry first
            // wins and the loser is a guaranteed no-op. Cancelling the TCS directly could win the race
            // against TryComplete and discard an already-delivered response as a spurious timeout.
            var callerCancellation = ct.CanBeCanceled
                ? ct.Register(static state => ((IPendingResponse)state!).CancelByCaller(), pending)
                : default;
            using (callerCancellation)
            {
                _pending.StartTimeout(pending, _timeout);
                try
                {
                    received = await pending.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (pending.CancellationKind == PendingCancellationKind.Timeout)
                {
                    if (requestSent)
                    {
                        _cancelFrames.TrySend(messageId);
                    }

                    ct.ThrowIfCancellationRequested();
                    throw new ShaRpcTimeoutException($"Request to {service}.{method} timed out.");
                }
                catch (OperationCanceledException) when (pending.CancellationKind == PendingCancellationKind.Caller)
                {
                    if (requestSent)
                    {
                        _cancelFrames.TrySend(messageId);
                    }

                    ct.ThrowIfCancellationRequested();
                    throw;
                }
            }

            if (!received.Response.IsSuccess)
            {
                var error = new ShaRpcRemoteException(
                    received.Response.ErrorMessage ?? "Unknown error",
                    received.Response.ErrorType ?? "Unknown");
                received.Dispose();
                throw error;
            }

            received.AttachOutboundStreams(outboundStreams);
            consumed = true;
            return received;
        }
        finally
        {
            _pending.Remove(messageId, pending, consumed);
            ReleasePendingSlot();
            if (!consumed)
            {
                await outboundStreams.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

}
