using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Generated;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;
/// <summary>
/// One symmetric side of a ShaRPC connection. A peer can provide local services and get proxies
/// for remote services over one demuxed read loop.
/// </summary>
public sealed class RpcPeer : IAsyncDisposable, IRpcInvoker
{
    private readonly IRpcChannel _channel;
    private readonly RpcPeerInboundDispatcher _inbound;
    private readonly RpcPeerOutboundInvoker _outbound;
    private readonly RpcPeerFrameProcessor _frameProcessor;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _cts;
    private Task? _readLoop;
    private int _started;
    private int _closed;
    private int _disposed;

    private RpcPeer(IRpcChannel channel, ISerializer serializer, RpcPeerOptions options)
    {
        _channel = channel;
        _inbound = new RpcPeerInboundDispatcher(serializer, options, SendRawAsync, RaiseProtocolError);
        _outbound = new RpcPeerOutboundInvoker(serializer, options.RequestTimeout, EnsureStarted, SendRawAsync);
        _frameProcessor = new RpcPeerFrameProcessor(_inbound, _outbound, RaiseProtocolError);
    }

    /// <summary>Creates a peer over <paramref name="channel"/>. Call <see cref="Start"/> to begin
    /// the read loop (invoking a method also starts it implicitly).</summary>
    public static RpcPeer Over(IRpcChannel channel, ISerializer serializer, RpcPeerOptions? options = null)
    {
        if (channel is null)
        {
            throw new ArgumentNullException(nameof(channel));
        }

        if (serializer is null)
        {
            throw new ArgumentNullException(nameof(serializer));
        }

        return new RpcPeer(channel, serializer, options ?? new RpcPeerOptions());
    }

    /// <summary>Gets whether the underlying channel is still connected.</summary>
    public bool IsConnected =>
        Volatile.Read(ref _disposed) == 0 &&
        Volatile.Read(ref _closed) == 0 &&
        _channel.IsConnected;

    /// <summary>The remote endpoint string of the underlying channel.</summary>
    public string RemoteEndpoint => _channel.RemoteEndpoint;

    /// <summary>Raised when the read loop ends after a remote close or read error; local close/dispose does not raise it.</summary>
    public event EventHandler<RpcDisconnectedEventArgs>? Disconnected;

    /// <summary>Raised when the read loop fails with a non-cancellation exception.</summary>
    public event EventHandler<RpcReadErrorEventArgs>? ReadError;

    /// <summary>Raised when a malformed or unsupported protocol frame is observed.</summary>
    public event EventHandler<RpcProtocolErrorEventArgs>? ProtocolError;

    /// <summary>Provides a local implementation of <typeparamref name="TService"/> for the other
    /// side to call.</summary>
    /// <remarks>Provided services are callable by any peer on this channel; enforce access
    /// control at the transport or application layer.</remarks>
    public RpcPeer Provide<TService>(TService implementation)
        where TService : class
    {
        if (implementation is null)
        {
            throw new ArgumentNullException(nameof(implementation));
        }

        return Provide(ShaRpcServiceRegistry.CreateDispatcher<TService>(implementation));
    }

    /// <summary>Provides a service via an explicit dispatcher.</summary>
    public RpcPeer Provide(IServiceDispatcher dispatcher)
    {
        if (dispatcher is null)
        {
            throw new ArgumentNullException(nameof(dispatcher));
        }

        _inbound.AddDispatcher(dispatcher);
        return this;
    }

    /// <summary>Creates a proxy to call <typeparamref name="TService"/> on the other side.</summary>
    public TService Get<TService>()
        where TService : class =>
        ShaRpcServiceRegistry.CreateProxy<TService>(this);

    /// <summary>Begins the read loop. Idempotent; safe to call from a fluent chain.</summary>
    public RpcPeer Start()
    {
        EnsureStarted();
        return this;
    }

    private void EnsureStarted()
    {
        lock (_lifecycleLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(RpcPeer));
            }

            if (Volatile.Read(ref _closed) != 0)
            {
                throw new ShaRpcConnectionException("Connection closed.");
            }

            if (Volatile.Read(ref _started) != 0)
            {
                return;
            }

            Interlocked.Exchange(ref _started, 1);
            _cts = new CancellationTokenSource();
            _inbound.Start(_cts.Token);
            _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));
        }
    }
    public Task<TResponse> InvokeAsync<TRequest, TResponse>(string service, string method, TRequest request, CancellationToken ct = default) =>
        _outbound.InvokeAsync<TRequest, TResponse>(service, method, request, ct);

    public Task<TResponse> InvokeAsync<TResponse>(string service, string method, CancellationToken ct = default) =>
        _outbound.InvokeAsync<TResponse>(service, method, ct);

    public Task InvokeAsync<TRequest>(string service, string method, TRequest request, CancellationToken ct = default) =>
        _outbound.InvokeAsync(service, method, request, ct);

    public Task InvokeAsync(string service, string method, CancellationToken ct = default) =>
        _outbound.InvokeAsync(service, method, ct);

    public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(string service, string instanceId, string method, TRequest request, CancellationToken ct = default) =>
        _outbound.InvokeOnInstanceAsync<TRequest, TResponse>(service, instanceId, method, request, ct);

    public Task<TResponse> InvokeOnInstanceAsync<TResponse>(string service, string instanceId, string method, CancellationToken ct = default) =>
        _outbound.InvokeOnInstanceAsync<TResponse>(service, instanceId, method, ct);

    public Task InvokeOnInstanceAsync<TRequest>(string service, string instanceId, string method, TRequest request, CancellationToken ct = default) =>
        _outbound.InvokeOnInstanceAsync(service, instanceId, method, request, ct);

    public Task InvokeOnInstanceAsync(string service, string instanceId, string method, CancellationToken ct = default) =>
        _outbound.InvokeOnInstanceAsync(service, instanceId, method, ct);

    private async Task SendRawAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _closed) != 0)
            {
                throw new ShaRpcConnectionException("Connection closed.");
            }

            await _channel.SendAsync(data, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        Exception? readError = null;
        try
        {
            while (!ct.IsCancellationRequested && _channel.IsConnected)
            {
                Payload frame;
                try
                {
                    frame = await _channel.ReceiveAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }

                if (frame.Length == 0)
                {
                    frame.Dispose();
                    break;
                }

                var disposeFrame = false;
                try
                {
                    disposeFrame = await _frameProcessor.ShouldDisposeAsync(frame, ct).ConfigureAwait(false);
                }
                finally
                {
                    if (disposeFrame)
                    {
                        frame.Dispose();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            readError = ex;
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                Interlocked.Exchange(ref _closed, 1);
                _outbound.FailPending(
                    readError is null
                        ? new ShaRpcConnectionException("Connection closed.")
                        : new ShaRpcConnectionException("Connection lost.", readError));
                await _inbound.StopAsync().ConfigureAwait(false);

                if (readError is not null)
                {
                    RpcEventHandlerInvoker.Raise(
                        ReadError,
                        this,
                        new RpcReadErrorEventArgs(_channel.RemoteEndpoint, readError));
                }

                RpcEventHandlerInvoker.Raise(
                    Disconnected,
                    this,
                    new RpcDisconnectedEventArgs(_channel.RemoteEndpoint, readError));
            }
        }
    }

    /// <summary>Closes the peer by disposing it. The token is checked before disposal starts;
    /// closed peers cannot be restarted.</summary>
    public Task CloseAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return DisposeAsync().AsTask();
    }

    public async ValueTask DisposeAsync()
    {
        Task? readLoop;
        CancellationTokenSource? cts;
        lock (_lifecycleLock)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Interlocked.Exchange(ref _closed, 1);
            cts = _cts;
            readLoop = _readLoop;
            cts?.Cancel();
        }

        await _inbound.StopAsync().ConfigureAwait(false);

        if (readLoop is not null)
        {
            try
            {
                await readLoop.ConfigureAwait(false);
            }
            catch
            {
                // Best-effort shutdown.
            }
        }

        _outbound.FailPending(new ShaRpcConnectionException("Connection closed."));

        cts?.Dispose();
        _sendLock.Dispose();
        await _channel.DisposeAsync().ConfigureAwait(false);
    }

    private void RaiseProtocolError(int messageId, MessageType messageType, string message) =>
        RpcEventHandlerInvoker.Raise(
            ProtocolError,
            this,
            new RpcProtocolErrorEventArgs(_channel.RemoteEndpoint, messageId, messageType, message));
}
