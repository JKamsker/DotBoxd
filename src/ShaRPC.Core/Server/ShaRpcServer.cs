using System.Collections.Concurrent;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core.Server;

/// <summary>
/// ShaRPC server that accepts connections and dispatches requests to registered services.
/// </summary>
public sealed class ShaRpcServer : IShaRpcServer
{
    private readonly IServerTransport _transport;
    private readonly ISerializer _serializer;
    private readonly ConcurrentDictionary<string, IServiceDispatcher> _dispatchers = new();
    private readonly ConcurrentDictionary<IConnection, Task> _connections = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private bool _disposed;

    public ShaRpcServer(IServerTransport transport, ISerializer serializer)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    /// <summary>
    /// Registers a service dispatcher.
    /// </summary>
    public void RegisterDispatcher(IServiceDispatcher dispatcher)
    {
        if (!_dispatchers.TryAdd(dispatcher.ServiceName, dispatcher))
        {
            throw new InvalidOperationException($"Service '{dispatcher.ServiceName}' is already registered.");
        }
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_cts != null)
        {
            throw new InvalidOperationException("Server is already running.");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await _transport.StartAsync(ct);
        _acceptTask = AcceptConnectionsAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_cts == null)
        {
            return;
        }

        _cts.Cancel();

        if (_acceptTask != null)
        {
            try
            {
                await _acceptTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        // Wait for all connections to close
        var connectionTasks = _connections.Values.ToArray();
        if (connectionTasks.Length > 0)
        {
            await Task.WhenAll(connectionTasks);
        }

        await _transport.StopAsync(ct);
        _cts.Dispose();
        _cts = null;
    }

    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var connection = await _transport.AcceptAsync(ct);
                // Each connection gets its own instance registry — sub-service identifiers
                // are scoped to the connection that created them.
                var registry = new InstanceRegistry();
                var connectionTask = HandleConnectionAsync(connection, registry, ct);
                _connections.TryAdd(connection, connectionTask);

                // Clean up completed connections
                _ = connectionTask.ContinueWith(_ => _connections.TryRemove(connection, out _), TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Log and continue accepting
            }
        }
    }

    private async Task HandleConnectionAsync(IConnection connection, IInstanceRegistry registry, CancellationToken ct)
    {
        try
        {
            while (connection.IsConnected && !ct.IsCancellationRequested)
            {
                using var data = await connection.ReceiveAsync(ct);
                if (data.Length == 0)
                {
                    break; // Connection closed
                }

                await ProcessMessageAsync(connection, registry, data, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception)
        {
            // Log error
        }
        finally
        {
            // Drop every sub-service instance tied to this connection so user code's
            // server-side state cannot outlive the connection that produced it.
            registry.ReleaseAll();
            await connection.DisposeAsync();
        }
    }

    private async Task ProcessMessageAsync(IConnection connection, IInstanceRegistry registry, Payload data, CancellationToken ct)
    {
        if (!MessageFramer.TryReadFrame(data.Memory, out var messageId, out var messageType, out var envelope, out var payload))
        {
            return;
        }

        if (messageType != MessageType.Request)
        {
            return; // Server only handles requests
        }

        // Safety invariant: `payload` is a zero-copy slice of the frame buffer (`data`), which the
        // caller keeps alive for the duration of this method. Dispatchers deserialize their
        // arguments synchronously from `payload` up front and never retain the memory, so the slice
        // never outlives `data`.
        var request = _serializer.Deserialize<RpcRequest>(envelope);
        RpcResponse response;
        Payload? resultPayload = null;

        try
        {
            if (!_dispatchers.TryGetValue(request.ServiceName, out var dispatcher))
            {
                response = new RpcResponse
                {
                    MessageId = messageId,
                    IsSuccess = false,
                    ErrorMessage = $"Service '{request.ServiceName}' not found.",
                    ErrorType = nameof(ShaRpcNotFoundException)
                };
            }
            else
            {
                // Route by instance: a non-null InstanceId means the call targets a
                // server-side sub-service that the client got a handle to earlier.
                resultPayload = request.InstanceId is null
                    ? await dispatcher.DispatchAsync(request.MethodName, payload, _serializer, registry, ct)
                    : await dispatcher.DispatchOnInstanceAsync(request.InstanceId, request.MethodName, payload, _serializer, registry, ct);

                response = new RpcResponse
                {
                    MessageId = messageId,
                    IsSuccess = true
                };
            }
        }
        catch (Exception ex)
        {
            response = new RpcResponse
            {
                MessageId = messageId,
                IsSuccess = false,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name
            };
        }

        try
        {
            var responseType = response.IsSuccess ? MessageType.Response : MessageType.Error;
            // The result payload is appended as raw trailing bytes after the serialized envelope,
            // copied into the frame before resultPayload is disposed in the finally below.
            using var responseFrame = resultPayload is null
                ? MessageFramer.FrameMessage(_serializer, messageId, responseType, response, ReadOnlySpan<byte>.Empty)
                : MessageFramer.FrameMessage(_serializer, messageId, responseType, response, resultPayload.Span);
            await connection.SendAsync(responseFrame.Memory, ct);
        }
        finally
        {
            resultPayload?.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync();
        await _transport.DisposeAsync();
    }
}
