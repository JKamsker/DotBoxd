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
    private readonly ShaRpcPendingRequests _pendingRequests = new();
    private readonly ShaRpcClientReceiveLoop _receiveLoop;
    private readonly TimeSpan _timeout;
    private int _messageIdCounter;
    private Task? _receiveTask;
    private CancellationTokenSource? _cts;
    private int _disposed;
    private int _connected;

    public ShaRpcClient(ITransport transport, ISerializer serializer, TimeSpan? timeout = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _receiveLoop = new ShaRpcClientReceiveLoop(_transport, _serializer, _pendingRequests);
    }

    public bool IsConnected => _transport.IsConnected;
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(ShaRpcClient));
        }

        if (Interlocked.Exchange(ref _connected, 1) != 0)
        {
            throw new InvalidOperationException("Already connected.");
        }

        try
        {
            await _transport.ConnectAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Exchange(ref _connected, 0);
            throw;
        }

        var cts = new CancellationTokenSource();
        var receiveTask = _receiveLoop.RunAsync(cts.Token);
        Volatile.Write(ref _cts, cts);
        Volatile.Write(ref _receiveTask, receiveTask);

        // DisposeAsync may have run while we awaited the transport connect, observing _cts and
        // _receiveTask while they were still null. Re-check and tear down the loop we just started
        // so it does not run on (and leak a CTS over) an already-disposed transport.
        if (Volatile.Read(ref _disposed) != 0)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // DisposeAsync already disposed the CTS.
            }

            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch
            {
                // Receive loop faulted against the disposing transport during teardown.
            }

            cts.Dispose();
            throw new ObjectDisposedException(nameof(ShaRpcClient));
        }
    }

    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId: null, ct).ConfigureAwait(false);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task<TResponse> InvokeAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId: null, ct).ConfigureAwait(false);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId: null, ct).ConfigureAwait(false);
    }

    public async Task InvokeAsync(
        string service,
        string method,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId: null, ct).ConfigureAwait(false);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId, ct).ConfigureAwait(false);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId, ct).ConfigureAwait(false);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId, ct).ConfigureAwait(false);
    }

    public async Task InvokeOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId, ct).ConfigureAwait(false);
    }

    private Task<ReceivedResponse> SendRequestAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        string? instanceId,
        CancellationToken ct)
    {
        var connection = EnsureConnected();
        var pending = ReservePendingRequest();
        try
        {
            var envelope = ShaRpcClientFrameHelpers.CreateEnvelope(pending.MessageId, service, method, instanceId);

            var frame = MessageFramer.FrameRequest(
                _serializer,
                pending.MessageId,
                MessageType.Request,
                envelope,
                request);
            return SendFrameAndAwaitAsync(
                pending.MessageId,
                pending.Completion,
                frame,
                connection,
                service,
                method,
                ct);
        }
        catch
        {
            _pendingRequests.Remove(pending.MessageId, pending.Completion.Task, consumed: true);
            throw;
        }
    }

    private Task<ReceivedResponse> SendRequestAsync(
        string service,
        string method,
        string? instanceId,
        CancellationToken ct)
    {
        var connection = EnsureConnected();
        var pending = ReservePendingRequest();
        try
        {
            var envelope = ShaRpcClientFrameHelpers.CreateEnvelope(pending.MessageId, service, method, instanceId);

            var frame = MessageFramer.FrameMessage(
                _serializer,
                pending.MessageId,
                MessageType.Request,
                envelope,
                ReadOnlySpan<byte>.Empty);
            return SendFrameAndAwaitAsync(
                pending.MessageId,
                pending.Completion,
                frame,
                connection,
                service,
                method,
                ct);
        }
        catch
        {
            _pendingRequests.Remove(pending.MessageId, pending.Completion.Task, consumed: true);
            throw;
        }
    }

    private (int MessageId, TaskCompletionSource<ReceivedResponse> Completion) ReservePendingRequest()
    {
        const int maxAttempts = 8192;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var messageId = Interlocked.Increment(ref _messageIdCounter);
            if (messageId != 0 && _pendingRequests.TryAdd(messageId, out var tcs))
            {
                return (messageId, tcs);
            }
        }

        throw new ShaRpcException("Unable to reserve a request message id.");
    }

    private IConnection EnsureConnected()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(ShaRpcClient));
        }

        var connection = _transport.Connection;
        if (connection == null || !_transport.IsConnected)
        {
            throw new ShaRpcConnectionException("Not connected to server.");
        }

        return connection;
    }

    private async Task<ReceivedResponse> SendFrameAndAwaitAsync(
        int messageId,
        TaskCompletionSource<ReceivedResponse> tcs,
        Payload frame,
        IConnection connection,
        string service,
        string method,
        CancellationToken ct)
    {
        var consumed = false;
        var requestSent = false;
        try
        {
            using (frame)
            {
                await connection.SendAsync(frame.Memory, ct).ConfigureAwait(false);
                requestSent = true;
            }

            using var timeoutCts = ct.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : new CancellationTokenSource();
            timeoutCts.CancelAfter(_timeout);

            ReceivedResponse received;
            using (timeoutCts.Token.Register(
                static state => ((TaskCompletionSource<ReceivedResponse>)state!).TrySetCanceled(),
                tcs))
            {
                try
                {
                    received = await tcs.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    if (requestSent)
                    {
                        _ = ShaRpcClientFrameHelpers.SendCancelFrameAsync(connection, messageId)
                            .ContinueWith(
                                static _ => { },
                                TaskContinuationOptions.OnlyOnFaulted);
                    }

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

            consumed = true;
            return received;
        }
        finally
        {
            _pendingRequests.Remove(messageId, tcs.Task, consumed);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var cts = Volatile.Read(ref _cts);
        var receiveTask = Volatile.Read(ref _receiveTask);

        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A concurrent ConnectAsync teardown already disposed the CTS.
            }
        }

        if (receiveTask != null)
        {
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        _pendingRequests.FailAll(new ShaRpcConnectionException("Connection closed."));

        cts?.Dispose();
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
