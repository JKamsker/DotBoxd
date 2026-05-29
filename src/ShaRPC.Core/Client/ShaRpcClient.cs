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
        using var requestPayload = _serializer.SerializeToPayload(request);
        using var received = await SendRequestAsync(service, method, requestPayload.Memory, instanceId: null, ct);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task<TResponse> InvokeAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, ReadOnlyMemory<byte>.Empty, instanceId: null, ct);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var requestPayload = _serializer.SerializeToPayload(request);
        using var received = await SendRequestAsync(service, method, requestPayload.Memory, instanceId: null, ct);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var requestPayload = _serializer.SerializeToPayload(request);
        using var received = await SendRequestAsync(service, method, requestPayload.Memory, instanceId, ct);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, ReadOnlyMemory<byte>.Empty, instanceId, ct);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var requestPayload = _serializer.SerializeToPayload(request);
        using var received = await SendRequestAsync(service, method, requestPayload.Memory, instanceId, ct);
    }

    private async Task<ReceivedResponse> SendRequestAsync(
        string service,
        string method,
        ReadOnlyMemory<byte> payload,
        string? instanceId,
        CancellationToken ct)
    {
        if (_transport.Connection == null || !_transport.IsConnected)
        {
            throw new ShaRpcConnectionException("Not connected to server.");
        }

        var messageId = Interlocked.Increment(ref _messageIdCounter);
        var request = new RpcRequest
        {
            MessageId = messageId,
            ServiceName = service,
            MethodName = method,
            InstanceId = instanceId,
        };

        var tcs = new TaskCompletionSource<ReceivedResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests.TryAdd(messageId, tcs);

        // Ownership note: the received frame is handed to us through `tcs`. We only return it to the
        // caller (transferring ownership) once `consumed` is set; on every other path the finally
        // disposes the frame via DisposeResultWhenAvailable, so a response that races in after a
        // timeout/cancel can never leak its rented buffer.
        var consumed = false;
        try
        {
            using (var frame = MessageFramer.FrameMessage(_serializer, messageId, MessageType.Request, request, payload.Span))
            {
                await _transport.Connection.SendAsync(frame.Memory, ct);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_timeout);

            try
            {
                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(_timeout, timeoutCts.Token));

                if (completedTask != tcs.Task)
                {
                    throw new ShaRpcTimeoutException($"Request to {service}.{method} timed out.");
                }

                var received = await tcs.Task;

                if (!received.Response.IsSuccess)
                {
                    throw new ShaRpcRemoteException(
                        received.Response.ErrorMessage ?? "Unknown error",
                        received.Response.ErrorType ?? "Unknown");
                }

                consumed = true;
                return received;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new ShaRpcTimeoutException($"Request to {service}.{method} timed out.");
            }
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
