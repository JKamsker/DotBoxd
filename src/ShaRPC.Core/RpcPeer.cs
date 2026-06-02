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
    private readonly RpcPeerReadLoop _readLoopRunner;
    private readonly RpcPeerSender _sender;
    private readonly object _lifecycleLock = new();
    private readonly IServiceProvider? _serviceProvider;
    private CancellationTokenSource? _cts;
    private Task? _readLoop;
    private Task? _disposeTask;
    private int _started;
    private int _closed;
    private int _disposed;

    private RpcPeer(IRpcChannel channel, ISerializer serializer, RpcPeerOptions options)
    {
        _channel = channel;
        _sender = new RpcPeerSender(channel, () => Volatile.Read(ref _closed) != 0);
        _inbound = new RpcPeerInboundDispatcher(
            serializer,
            options,
            _sender.SendAsync,
            RaiseProtocolError,
            RaiseDispatchError);
        _outbound = new RpcPeerOutboundInvoker(serializer, options, EnsureStarted, _sender.SendAsync);
        var frameProcessor = new RpcPeerFrameProcessor(_inbound, _outbound, RaiseProtocolError);
        _readLoopRunner = new RpcPeerReadLoop(
            channel,
            _inbound,
            _outbound,
            frameProcessor,
            MarkClosed,
            RaiseReadError,
            RaiseDisconnected);
        _serviceProvider = options.ServiceProvider;
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
    /// <summary>
    /// Raised when the read loop ends after a remote close or read error; local close/dispose does
    /// not raise it. Handlers run on the teardown path and should not block.
    /// </summary>
    public event EventHandler<RpcDisconnectedEventArgs>? Disconnected;

    /// <summary>Raised when the read loop fails with a non-cancellation exception.</summary>
    public event EventHandler<RpcReadErrorEventArgs>? ReadError;

    /// <summary>Raised when a malformed or unsupported protocol frame is observed.</summary>
    public event EventHandler<RpcProtocolErrorEventArgs>? ProtocolError;

    /// <summary>Raised when inbound request dispatch or response sending fails.</summary>
    public event EventHandler<RpcDispatchErrorEventArgs>? DispatchError;

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

    /// <summary>Resolves and provides a local implementation of <typeparamref name="TService"/> from the configured service provider.</summary>
    public RpcPeer Provide<TService>()
        where TService : class
    {
        if (_serviceProvider is null)
        {
            throw new InvalidOperationException("No ServiceProvider configured on RpcPeerOptions.");
        }

        if (_serviceProvider.GetService(typeof(TService)) is not TService implementation)
        {
            throw new InvalidOperationException($"Service provider did not resolve service '{typeof(TService).FullName}'.");
        }

        return Provide(implementation);
    }

    /// <summary>Provides a service via an explicit dispatcher.</summary>
    public RpcPeer Provide(IServiceDispatcher dispatcher)
    {
        if (dispatcher is null)
        {
            throw new ArgumentNullException(nameof(dispatcher));
        }

        lock (_lifecycleLock)
        {
            if (_disposed != 0)
            {
                throw new ObjectDisposedException(nameof(RpcPeer));
            }

            if (_closed != 0)
            {
                throw new ShaRpcConnectionException("Connection closed.");
            }

            if (_started != 0)
            {
                throw new InvalidOperationException("Services must be provided before the peer starts.");
            }

            _inbound.AddDispatcher(dispatcher);
        }

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
            if (_disposed != 0)
            {
                throw new ObjectDisposedException(nameof(RpcPeer));
            }

            if (_closed != 0)
            {
                throw new ShaRpcConnectionException("Connection closed.");
            }

            if (_started != 0)
            {
                return;
            }

            Interlocked.Exchange(ref _started, 1);
            _cts = new CancellationTokenSource();
            _inbound.Start(_cts.Token);
            _readLoop = Task.Run(() => _readLoopRunner.RunAsync(_cts.Token));
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

    /// <summary>Closes the peer by disposing it; closed peers cannot be restarted.</summary>
    public Task CloseAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return RpcTaskWaiter.WaitAsync(DisposeAsync().AsTask(), ct);
    }

    public ValueTask DisposeAsync()
    {
        Task? readLoop;
        CancellationTokenSource? cts;
        Task disposeTask;
        lock (_lifecycleLock)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _disposed = 1;
            Interlocked.Exchange(ref _closed, 1);
            cts = _cts;
            readLoop = _readLoop;
            cts?.Cancel();
            disposeTask = DisposeCoreAsync(readLoop, cts);
            _disposeTask = disposeTask;
        }

        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync(Task? readLoop, CancellationTokenSource? cts)
    {
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

        await _outbound.StopCancelFramesAsync().ConfigureAwait(false);
        _outbound.FailPending(new ShaRpcConnectionException("Connection closed."));
        await _inbound.StopAsync().ConfigureAwait(false);

        _sender.Dispose();
        await _channel.DisposeAsync().ConfigureAwait(false);
        cts?.Dispose();
    }

    private void RaiseProtocolError(
        int messageId,
        MessageType messageType,
        string message,
        Exception? error) =>
        RpcEventHandlerInvoker.Raise(
            ProtocolError,
            this,
            new RpcProtocolErrorEventArgs(_channel.RemoteEndpoint, messageId, messageType, message, error));

    private void RaiseDispatchError(RpcPeerInboundRequest inbound, Exception error) =>
        RpcEventHandlerInvoker.Raise(
            DispatchError,
            this,
            new RpcDispatchErrorEventArgs(
                _channel.RemoteEndpoint,
                inbound.MessageId,
                inbound.Request.ServiceName,
                inbound.Request.MethodName,
                inbound.Request.InstanceId,
                error));

    private void MarkClosed()
    {
        lock (_lifecycleLock)
        {
            Volatile.Write(ref _closed, 1);
        }
    }

    private void RaiseReadError(Exception error) =>
        RpcEventHandlerInvoker.Raise(
            ReadError,
            this,
            new RpcReadErrorEventArgs(_channel.RemoteEndpoint, error));

    private void RaiseDisconnected(Exception? error) =>
        RpcEventHandlerInvoker.Raise(
            Disconnected,
            this,
            new RpcDisconnectedEventArgs(_channel.RemoteEndpoint, error));
}
