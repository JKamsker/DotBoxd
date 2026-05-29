using System.Collections.Concurrent;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core.Client;

/// <summary>
/// ShaRPC client that sends requests and receives responses.
/// </summary>
public sealed class ShaRpcClient : IShaRpcClient
{
    private readonly ITransport _transport;
    private readonly ISerializer _serializer;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ReceivedResponse>> _pendingRequests = new();
    private readonly TimeSpan _timeout;
    private int _messageIdCounter;
    private Task? _receiveTask;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public ShaRpcClient(ITransport transport, ISerializer serializer, TimeSpan? timeout = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public bool IsConnected => _transport.IsConnected;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _transport.ConnectAsync(ct);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _receiveTask = ReceiveLoopAsync(_cts.Token);
    }

    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId: null, ct);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task<TResponse> InvokeAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId: null, ct);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId: null, ct);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId, ct);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId, ct);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId, ct);
    }

    private async Task<ReceivedResponse> SendRequestAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        string? instanceId,
        CancellationToken ct)
    {
        var connection = EnsureConnected();
        var messageId = Interlocked.Increment(ref _messageIdCounter);
        var envelope = CreateEnvelope(messageId, service, method, instanceId);

        // Serialize the argument straight into the frame buffer instead of into a separate pooled
        // payload that then has to be copied in behind the envelope.
        var frame = MessageFramer.FrameRequest(_serializer, messageId, MessageType.Request, envelope, request);
        return await SendFrameAndAwaitAsync(messageId, frame, connection, service, method, ct);
    }

    private async Task<ReceivedResponse> SendRequestAsync(
        string service,
        string method,
        string? instanceId,
        CancellationToken ct)
    {
        var connection = EnsureConnected();
        var messageId = Interlocked.Increment(ref _messageIdCounter);
        var envelope = CreateEnvelope(messageId, service, method, instanceId);

        var frame = MessageFramer.FrameMessage(_serializer, messageId, MessageType.Request, envelope, ReadOnlySpan<byte>.Empty);
        return await SendFrameAndAwaitAsync(messageId, frame, connection, service, method, ct);
    }

    private IConnection EnsureConnected()
    {
        var connection = _transport.Connection;
        if (connection == null || !_transport.IsConnected)
        {
            throw new ShaRpcConnectionException("Not connected to server.");
        }

        return connection;
    }

    private static RpcRequest CreateEnvelope(int messageId, string service, string method, string? instanceId) =>
        new()
        {
            MessageId = messageId,
            ServiceName = service,
            MethodName = method,
            InstanceId = instanceId,
        };

    /// <summary>
    /// Registers the pending request, sends the already-framed request, and awaits the response.
    /// Ownership of <paramref name="frame"/> transfers here and it is disposed once sent. The response
    /// frame handed back through the pending TCS is only returned to the caller once the success check
    /// passes; on every other path it is disposed via <see cref="DisposeResultWhenAvailable"/>, so a
    /// response that races in after a timeout/cancel can never leak its rented buffer.
    /// </summary>
    private async Task<ReceivedResponse> SendFrameAndAwaitAsync(
        int messageId,
        Payload frame,
        IConnection connection,
        string service,
        string method,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<ReceivedResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests.TryAdd(messageId, tcs);

        var consumed = false;
        try
        {
            using (frame)
            {
                await connection.SendAsync(frame.Memory, ct);
            }

            var received = await AwaitResponseAsync(tcs, service, method, ct);
            consumed = true;
            return received;
        }
        finally
        {
            _pendingRequests.TryRemove(messageId, out _);
            if (!consumed)
            {
                DisposeResultWhenAvailable(tcs.Task);
            }
        }
    }

    private async Task<ReceivedResponse> AwaitResponseAsync(
        TaskCompletionSource<ReceivedResponse> tcs,
        string service,
        string method,
        CancellationToken ct)
    {
        // A linked source is only needed when the caller's token can actually fire; otherwise a
        // plain source carrying just the timeout avoids the linking overhead.
        using var timeoutCts = ct.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : new CancellationTokenSource();
        timeoutCts.CancelAfter(_timeout);

        ReceivedResponse received;
        // Completing the pending TCS from the token registration—rather than racing tcs.Task against
        // Task.Delay through Task.WhenAny—avoids allocating the delay task, its timer, and the WhenAny
        // array on every call. The static callback plus state argument keeps the registration closure-free.
        using (timeoutCts.Token.Register(
            static state => ((TaskCompletionSource<ReceivedResponse>)state!).TrySetCanceled(),
            tcs))
        {
            try
            {
                received = await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                // The linked token fires for both the caller's cancellation and the timeout;
                // re-throw the former as-is and map the latter to a timeout.
                ct.ThrowIfCancellationRequested();
                throw new ShaRpcTimeoutException($"Request to {service}.{method} timed out.");
            }
        }

        if (!received.Response.IsSuccess)
        {
            throw new ShaRpcRemoteException(
                received.Response.ErrorMessage ?? "Unknown error",
                received.Response.ErrorType ?? "Unknown");
        }

        return received;
    }

    /// <summary>
    /// Disposes the frame carried by a response the caller has abandoned (timeout, cancellation, or
    /// a remote error). Handles the case where the response has not arrived yet by disposing it on
    /// completion. A faulted or cancelled task carries no frame, so nothing is disposed.
    /// </summary>
    private static void DisposeResultWhenAvailable(Task<ReceivedResponse> task)
    {
        if (task.IsCompleted)
        {
            if (task.Status == TaskStatus.RanToCompletion)
            {
                task.Result.Dispose();
            }

            return;
        }

        _ = task.ContinueWith(
            static t =>
            {
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    t.Result.Dispose();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _transport.IsConnected)
            {
                var connection = _transport.Connection;
                if (connection == null)
                {
                    break;
                }

                var frame = await connection.ReceiveAsync(ct);
                if (frame.Length == 0)
                {
                    frame.Dispose();
                    break;
                }

                // Safety invariant: `payload` is a zero-copy slice of `frame`. Ownership of `frame`
                // is transferred to the ReceivedResponse carrier and ultimately to the awaiting
                // caller, which disposes it after deserializing the payload. If the frame is not
                // handed off (unparseable, wrong type, or no matching/abandoned request) we dispose
                // it here so the rented buffer is always returned exactly once.
                var handedOff = false;
                try
                {
                    if (!MessageFramer.TryReadFrame(frame.Memory, out var messageId, out var messageType, out var envelope, out var payload))
                    {
                        continue;
                    }

                    if (messageType != MessageType.Response && messageType != MessageType.Error)
                    {
                        continue;
                    }

                    var response = _serializer.Deserialize<RpcResponse>(envelope);
                    if (_pendingRequests.TryGetValue(messageId, out var tcs))
                    {
                        var received = new ReceivedResponse(response, payload, frame);
                        if (!tcs.TrySetResult(received))
                        {
                            // The caller already gave up; release the frame we just took ownership of.
                            received.Dispose();
                        }

                        handedOff = true;
                    }
                }
                finally
                {
                    if (!handedOff)
                    {
                        frame.Dispose();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        catch (Exception ex)
        {
            // complete all pending requests with error
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetException(new ShaRpcConnectionException("Connection lost.", ex));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _cts?.Cancel();

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch
            {
                // ignore
            }
        }

        // Complete all pending requests
        foreach (var kvp in _pendingRequests)
        {
            kvp.Value.TrySetCanceled();
        }

        _cts?.Dispose();
        await _transport.DisposeAsync();
    }

    /// <summary>
    /// Carries a deserialized <see cref="RpcResponse"/> together with the zero-copy payload slice and
    /// the frame buffer that backs it. Disposing returns the rented frame to the pool exactly once.
    /// </summary>
    private sealed class ReceivedResponse : IDisposable
    {
        private Payload? _frame;

        public ReceivedResponse(RpcResponse response, ReadOnlyMemory<byte> payload, Payload frame)
        {
            Response = response;
            Payload = payload;
            _frame = frame;
        }

        public RpcResponse Response { get; }

        public ReadOnlyMemory<byte> Payload { get; }

        public void Dispose() => Interlocked.Exchange(ref _frame, null)?.Dispose();
    }
}
