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
    private readonly ShaRpcServerResponseBuilder _responseBuilder;
    private readonly ConcurrentDictionary<IConnection, Task> _connections = new();
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private Task? _stopTask;
    private int _disposed;
    private int _started;

    public ShaRpcServer(IServerTransport transport, ISerializer serializer)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _responseBuilder = new ShaRpcServerResponseBuilder(_serializer, _dispatchers);
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
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("Already started.");
        }

        // Assign _cts under the lock before the await so a concurrent Stop/Dispose observes it, and
        // re-check after the await before launching the accept loop. Otherwise a stop that runs
        // while the transport is starting can null _cts (StartAsync then NREs on _cts.Token) and
        // leave an accept loop orphaned on an already-stopped transport.
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_lifecycleLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                cts.Dispose();
                throw new ObjectDisposedException(nameof(ShaRpcServer));
            }

            _cts = cts;
        }

        await _transport.StartAsync(ct).ConfigureAwait(false);

        var stopStartedTransport = false;
        Exception? startFailure = null;
        var disposeCts = false;
        lock (_lifecycleLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                var ownsCts = ReferenceEquals(_cts, cts);
                disposeCts = ownsCts && _stopTask is null;
                if (ownsCts)
                {
                    _cts = null;
                    _acceptTask = null;
                }

                stopStartedTransport = true;
                startFailure = new ObjectDisposedException(nameof(ShaRpcServer));
            }
            else if (!ReferenceEquals(_cts, cts) || _stopTask is not null || cts.IsCancellationRequested)
            {
                // A concurrent stop already ran (or is running) and owns the CTS lifecycle.
                stopStartedTransport = true;
            }
            else
            {
                _acceptTask = AcceptConnectionsAsync(cts.Token);
                return;
            }
        }

        if (stopStartedTransport)
        {
            await _transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        if (startFailure is not null)
        {
            if (disposeCts)
            {
                cts.Dispose();
            }

            throw startFailure;
        }

        throw new InvalidOperationException("Server start was stopped before it completed.");
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        lock (_lifecycleLock)
        {
            if (_stopTask is not null) return _stopTask;
            if (_cts is null) return Task.CompletedTask;
            return _stopTask = StopCoreAsync(_cts, _acceptTask, ct);
        }
    }

    private async Task StopCoreAsync(CancellationTokenSource cts, Task? acceptTask, CancellationToken ct)
    {
        var completed = false;
        try
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A prior failed start path disposed the CTS.
            }

            if (acceptTask != null)
            {
                try
                {
                    await acceptTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }

            var connectionTasks = _connections.Values.ToArray();
            if (connectionTasks.Length > 0)
            {
                try
                {
                    await Task.WhenAll(connectionTasks).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    RpcDiagnostics.Report("Connection handler fault during shutdown", ex);
                }
            }

            await _transport.StopAsync(ct).ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            try
            {
                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }

            lock (_lifecycleLock)
            {
                if (ReferenceEquals(_cts, cts))
                {
                    _stopTask = null;
                    if (completed)
                    {
                        _cts = null;
                        _acceptTask = null;
                    }
                }
            }
        }
    }

    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var connection = await _transport.AcceptAsync(ct).ConfigureAwait(false);
                var registry = new InstanceRegistry();
                var connectionTask = HandleConnectionAsync(connection, registry, ct);
                _connections.TryAdd(connection, connectionTask);

                _ = connectionTask.ContinueWith(
                    _ => _connections.TryRemove(connection, out _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                try
                {
                    await Task.Delay(50, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }
        }
    }

    private async Task HandleConnectionAsync(IConnection connection, IInstanceRegistry registry, CancellationToken ct)
    {
        var activeRequests = new ConcurrentDictionary<int, CancellationTokenSource>();
        var activeTasks = new ConcurrentDictionary<int, Task>();
        var concurrency = new SemaphoreSlim(1024, 1024);

        try
        {
            while (connection.IsConnected && !ct.IsCancellationRequested)
            {
                var data = await connection.ReceiveAsync(ct).ConfigureAwait(false);
                if (data.Length == 0)
                {
                    data.Dispose();
                    break;
                }

                try
                {
                    await concurrency.WaitAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    data.Dispose();
                    throw;
                }

                ProcessMessage(connection, registry, data, activeRequests, activeTasks, concurrency, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Connection handler failed", ex);
        }
        finally
        {
            foreach (var request in activeRequests.Values)
            {
                SafeCancel(request);
            }

            await WaitForActiveRequestsAsync(activeTasks.Values).ConfigureAwait(false);

            concurrency.Dispose();
            registry.ReleaseAll();
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ProcessMessage(
        IConnection connection,
        IInstanceRegistry registry,
        Payload data,
        ConcurrentDictionary<int, CancellationTokenSource> activeRequests,
        ConcurrentDictionary<int, Task> activeTasks,
        SemaphoreSlim concurrency,
        CancellationToken ct)
    {
        if (!MessageFramer.TryReadFrameHeader(data.Memory, out var messageId, out var messageType))
        {
            data.Dispose();
            concurrency.Release();
            return;
        }

        if (messageType == MessageType.Cancel)
        {
            if (activeRequests.TryGetValue(messageId, out var requestCts))
            {
                SafeCancel(requestCts);
            }

            data.Dispose();
            concurrency.Release();
            return;
        }

        if (messageType != MessageType.Request ||
            !MessageFramer.TryReadFrame(data.Memory, out _, out _, out var envelope, out var payload))
        {
            data.Dispose();
            concurrency.Release();
            return;
        }

        RpcRequest request;
        try
        {
            request = _serializer.Deserialize<RpcRequest>(envelope);
        }
        catch (Exception ex)
        {
            data.Dispose();
            concurrency.Release();
            RpcDiagnostics.Report("Request envelope deserialization failed", ex);
            try
            {
                var errorFrame = MessageFramer.FrameMessage(
                    _serializer,
                    messageId,
                    MessageType.Error,
                    new RpcResponse
                    {
                        MessageId = messageId,
                        IsSuccess = false,
                        ErrorMessage = "Protocol error: malformed request envelope.",
                        ErrorType = RpcErrorTypes.ProtocolError,
                    },
                    ReadOnlySpan<byte>.Empty);
                var ownershipTransferred = false;
                try
                {
                    // Transfer ownership to the send task so the frame is not disposed while
                    // SendAsync is still reading from its memory.
                    var sendTask = connection.SendAsync(errorFrame.Memory, ct);
                    _ = sendTask.ContinueWith(
                        static (t, state) =>
                        {
                            ((Payload)state!).Dispose();
                            if (t.IsFaulted)
                                RpcDiagnostics.Report("Error frame send failed", t.Exception!.GetBaseException());
                        },
                        errorFrame,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    ownershipTransferred = true;
                }
                finally
                {
                    // SendAsync threw synchronously (e.g. the connection closed) before ownership
                    // transferred to the continuation; dispose the rented frame here so it returns
                    // to the pool.
                    if (!ownershipTransferred)
                    {
                        errorFrame.Dispose();
                    }
                }
            }
            catch
            {
                // Best-effort error response.
            }
            return;
        }

        var dispatchCts = new CancellationTokenSource();
        if (!activeRequests.TryAdd(messageId, dispatchCts))
        {
            dispatchCts.Dispose();
            data.Dispose();
            concurrency.Release();
            return;
        }

        var task = ProcessRequestAsync(
            connection,
            registry,
            data,
            request,
            messageId,
            payload,
            activeRequests,
            activeTasks,
            concurrency,
            dispatchCts);
        activeTasks.TryAdd(messageId, task);
        if (task.IsCompleted)
        {
            activeTasks.TryRemove(messageId, out _);
        }
    }

    private async Task ProcessRequestAsync(
        IConnection connection,
        IInstanceRegistry registry,
        Payload data,
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> payload,
        ConcurrentDictionary<int, CancellationTokenSource> activeRequests,
        ConcurrentDictionary<int, Task> activeTasks,
        SemaphoreSlim concurrency,
        CancellationTokenSource requestCts)
    {
        try
        {
            using (data)
            {
                using var responseFrame = await _responseBuilder.BuildAsync(
                    request,
                    messageId,
                    payload,
                    registry,
                    requestCts.Token).ConfigureAwait(false);
                await connection.SendAsync(responseFrame.Memory, requestCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
        {
            // Remote cancellation or server shutdown; no response frame is sent for cancelled work.
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Request dispatch failed", ex);
            try
            {
                var error = RpcErrors.FromException(ex);
                using var errorFrame = MessageFramer.FrameMessage(
                    _serializer,
                    messageId,
                    MessageType.Error,
                    new RpcResponse
                    {
                        MessageId = messageId,
                        IsSuccess = false,
                        ErrorMessage = error.Message,
                        ErrorType = error.Type,
                    },
                    ReadOnlySpan<byte>.Empty);
                await connection.SendAsync(errorFrame.Memory, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort error response.
            }
        }
        finally
        {
            activeRequests.TryRemove(messageId, out _);
            activeTasks.TryRemove(messageId, out _);
            concurrency.Release();
            requestCts.Dispose();
        }
    }

    private static async Task WaitForActiveRequestsAsync(IEnumerable<Task> activeRequests)
    {
        try
        {
            await Task.WhenAll(activeRequests).ConfigureAwait(false);
        }
        catch
        {
            // Individual request tasks observe and swallow dispatch/send failures.
        }
    }

    private static void SafeCancel(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The request completed while the connection was closing.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
