using DotBoxD.Services.Client;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Generated;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Peer;
/// <summary>
/// One symmetric side of a DotBoxD connection. A peer can provide local services and get proxies
/// for remote services over one demuxed read loop.
/// </summary>
public sealed partial class RpcPeer : IAsyncDisposable, IRpcInvoker
{
    private readonly IRpcChannel _channel;
    private readonly RpcPeerInboundDispatcher _inbound;
    private readonly RpcPeerOutboundInvoker _outbound;
    private readonly RpcPeerReadLoop _readLoopRunner;
    private readonly RpcPeerSender _sender;
    private readonly RpcStreamManager _streams;
    private readonly object _lifecycleLock = new();
    private readonly IServiceProvider? _serviceProvider;
    private Dictionary<Type, ProxyCacheEntry>? _proxyCache;
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
        _streams = new RpcStreamManager(serializer, _sender.SendAsync, options.ExceptionTransformer, _sender.SendFrameValueAsync);
        _inbound = new RpcPeerInboundDispatcher(
            serializer,
            options,
            _streams,
            _sender.SendAsync,
            _sender.SendFrameValueAsync,
            RaiseProtocolError,
            RaiseDispatchError);
        _outbound = new RpcPeerOutboundInvoker(
            serializer,
            options,
            EnsureStarted,
            _sender.SendAsync,
            _sender.SendFrameValueAsync,
            _streams);
        var frameProcessor = new RpcPeerFrameProcessor(_inbound, _outbound, _streams, RaiseProtocolError);
        _readLoopRunner = new RpcPeerReadLoop(
            channel,
            _inbound,
            _outbound,
            _streams,
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

    /// <summary>
    /// Test seam: whether the read loop has been started (i.e. <see cref="Start"/>/an invoke has run).
    /// Lets a test assert the read loop has NOT begun while a host's <c>PeerConnected</c> handler runs.
    /// </summary>
    internal bool HasStarted => Volatile.Read(ref _started) != 0;

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

    /// <summary>
    /// Raised when an inbound request fails outside the service method itself — for example when
    /// the response or error frame cannot be sent. Exceptions thrown by a provided service method
    /// are not surfaced here; they are converted into an Error frame returned to the caller.
    /// </summary>
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

        return Provide(GeneratedServiceRegistry.CreateDispatcher<TService>(implementation));
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
                throw new ServiceConnectionException("Connection closed.");
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
        where TService : class
    {
        // Fail fast on a disposed peer rather than handing back a proxy that only throws on its first
        // call. Mirrors the disposal guard in Provide.
        lock (_lifecycleLock)
        {
            if (_disposed != 0)
            {
                throw new ObjectDisposedException(nameof(RpcPeer));
            }

            var serviceType = typeof(TService);
            if (_proxyCache is not null && _proxyCache.TryGetValue(serviceType, out var cached))
            {
                if (cached.RegistryVersion == GeneratedServiceRegistry.CurrentRegistrationVersion)
                {
                    return (TService)cached.Proxy;
                }

                if (GeneratedServiceRegistry.IsRegistrationCurrent(
                    serviceType,
                    cached.RegistrationVersion,
                    out var currentRegistryVersion))
                {
                    _proxyCache[serviceType] = new ProxyCacheEntry(
                        cached.Proxy,
                        cached.RegistrationVersion,
                        currentRegistryVersion);
                    return (TService)cached.Proxy;
                }
            }

            var proxy = GeneratedServiceRegistry.CreateProxy(serviceType, this, out var registrationVersion);
            (_proxyCache ??= new Dictionary<Type, ProxyCacheEntry>())[serviceType] =
                new ProxyCacheEntry(proxy, registrationVersion, registrationVersion);
            return (TService)proxy;
        }
    }

    private readonly record struct ProxyCacheEntry(
        object Proxy,
        long RegistrationVersion,
        long RegistryVersion);
}
