using ShaRPC.Core.Buffers;
using ShaRPC.Core.Client;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Streaming;
using System.IO.Pipelines;

namespace ShaRPC.Core;

internal sealed partial class RpcPeerOutboundInvoker : IRpcInvoker
{
    private readonly ISerializer _serializer;
    private readonly TimeSpan _timeout;
    private readonly int _maxPendingRequests;
    private readonly Action _ensureStarted;
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _sendAsync;
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
    {
        _serializer = serializer;
        _timeout = options.RequestTimeout;
        _maxPendingRequests = options.MaxPendingRequests;
        _ensureStarted = ensureStarted;
        _sendAsync = sendAsync;
        _streams = streams;
        _streamingCalls = new RpcPeerStreamingCalls(serializer);
        _cancelFrames = new RpcPeerCancelFrameSender(sendAsync);
    }

    public RpcStreamHandle ReserveStream(RpcStreamKind kind) =>
        _streams.ReserveOutbound(kind);

    public void ReleaseStream(RpcStreamHandle handle) =>
        _streams.ReleaseOutboundReservation(handle.StreamId);

    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId: null, streams: null, ct).ConfigureAwait(false);
        return DeserializeNonStreamingResponse<TResponse>(received);
    }

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

    public async Task<TResponse> InvokeAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId: null, ct).ConfigureAwait(false);
        return DeserializeNonStreamingResponse<TResponse>(received);
    }

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

    public bool TryCompleteResponse(int messageId, Payload frame)
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
            if (response.Stream is { } handle)
            {
                stream = _streams.RegisterInboundResponse(handle, CancellationToken.None);
            }

            var received = new ReceivedResponse(response, payload, frame, stream);
            if (!completion.TrySetResult(received))
            {
                received.Dispose();
            }

            return true;
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            return false;
        }
    }

    public void FailPending(Exception error) => _pending.FailAll(error);

    public Task StopCancelFramesAsync() => _cancelFrames.StopAsync();

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

    private async Task<ReceivedResponse> SendRequestAsync<TRequest>(
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
        catch
        {
            _streams.ReleaseOutboundReservations(streams);
            await DisposeStreamSourcesBestEffortAsync(streams).ConfigureAwait(false);
            throw;
        }

        (int MessageId, TaskCompletionSource<ReceivedResponse> Completion) pending;
        try
        {
            pending = ReservePendingRequest(ct);
        }
        catch
        {
            _streams.ReleaseOutboundReservations(streams);
            await DisposeStreamSourcesBestEffortAsync(streams).ConfigureAwait(false);
            throw;
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
        catch
        {
            // Registration or frame construction threw before SendFrameAndAwaitAsync took
            // ownership of the reserved slot, so release it here; otherwise the admission gate
            // leaks one slot per local setup failure and eventually rejects every call.
            _pending.Remove(pending.MessageId, pending.Completion.Task, consumed: true);
            ReleasePendingSlot();
            await outboundStreams.DisposeAsync().ConfigureAwait(false);
            if (!registeredStreams)
            {
                await DisposeStreamSourcesBestEffortAsync(streams).ConfigureAwait(false);
            }

            throw;
        }

        return await SendFrameAndAwaitAsync(
            pending.MessageId,
            pending.Completion,
            frame,
            service,
            method,
            outboundStreams,
            ct).ConfigureAwait(false);
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
                pending.Completion,
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
            _pending.Remove(pending.MessageId, pending.Completion.Task, consumed: true);
            ReleasePendingSlot();
            throw;
        }
    }

    private (int MessageId, TaskCompletionSource<ReceivedResponse> Completion) ReservePendingRequest(CancellationToken ct)
    {
        if (Interlocked.Increment(ref _pendingCount) > _maxPendingRequests)
        {
            Interlocked.Decrement(ref _pendingCount);
            throw new ShaRpcException("Maximum pending requests reached.");
        }

        try
        {
            for (var attempts = 0; attempts < _maxPendingRequests; attempts++)
            {
                ct.ThrowIfCancellationRequested();
                var messageId = NextMessageId(ct);
                if (messageId != 0 && _pending.TryAdd(messageId, out var tcs))
                {
                    return (messageId, tcs);
                }
            }

            throw new ShaRpcException("Unable to reserve a request message id.");
        }
        catch
        {
            Interlocked.Decrement(ref _pendingCount);
            throw;
        }
    }

    private void ReleasePendingSlot() => Interlocked.Decrement(ref _pendingCount);

    private int NextMessageId(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var messageId = Interlocked.Increment(ref _messageIdCounter);
            if (messageId != 0)
            {
                return messageId;
            }
        }
    }

    private async Task<ReceivedResponse> SendFrameAndAwaitAsync(
        int messageId,
        TaskCompletionSource<ReceivedResponse> tcs,
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

            using var timeoutCts = ct.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : new CancellationTokenSource();
            timeoutCts.CancelAfter(_timeout);

            ReceivedResponse received;
            // Cancel through the pending-request table rather than the TCS directly, so the timeout and
            // an incoming response race on a single atomic removal: whichever removes the entry first
            // wins and the loser is a guaranteed no-op. Cancelling the TCS directly could win the race
            // against TryComplete and discard an already-delivered response as a spurious timeout.
            var timeoutState = new TimeoutCancelState(_pending, messageId);
            using (timeoutCts.Token.Register(
                static state => ((TimeoutCancelState)state!).Cancel(),
                timeoutState))
            {
                try
                {
                    received = await tcs.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    if (requestSent)
                    {
                        _cancelFrames.TrySend(messageId);
                    }

                    ct.ThrowIfCancellationRequested();
                    throw new ShaRpcTimeoutException($"Request to {service}.{method} timed out.");
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
            _pending.Remove(messageId, tcs.Task, consumed);
            ReleasePendingSlot();
            if (!consumed)
            {
                await outboundStreams.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class TimeoutCancelState
    {
        private readonly ShaRpcPendingRequests _pending;
        private readonly int _messageId;

        public TimeoutCancelState(ShaRpcPendingRequests pending, int messageId)
        {
            _pending = pending;
            _messageId = messageId;
        }

        public void Cancel() => _pending.TryCancel(_messageId);
    }
}
