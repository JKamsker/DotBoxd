using ShaRPC.Core.Client;
using ShaRPC.Core.Generated;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core.Peer;

/// <summary>
/// Bidirectional ShaRPC endpoint over one duplex connection.
/// </summary>
[Obsolete("Use RpcPeer for new code. ShaRpcPeer will be removed in a future release.")]
public sealed class ShaRpcPeer : IAsyncDisposable
{
    private readonly DuplexConnectionSplitter _splitter;
    private readonly ShaRpcServer _server;
    private readonly ShaRpcClient _client;
    private int _disposed;

    private ShaRpcPeer(DuplexConnectionSplitter splitter, ShaRpcServer server, ShaRpcClient client)
    {
        _splitter = splitter;
        _server = server;
        _client = client;
    }

    /// <summary>
    /// Client used by generated proxies to call the remote peer.
    /// </summary>
    public IShaRpcClient Client => _client;

    /// <summary>
    /// Gets whether the shared connection is still connected.
    /// </summary>
    public bool IsConnected => Volatile.Read(ref _disposed) == 0 && _splitter.IsConnected;

    /// <summary>
    /// Raised when the shared read loop fails with a non-cancellation exception.
    /// </summary>
    public event Action<Exception>? ReadError;

    /// <summary>
    /// Raised when the remote side closes the shared connection. Handlers run on the teardown path
    /// and should not block.
    /// </summary>
    public event Action? Disconnected;

    /// <summary>
    /// Raised when the shared read loop ends, including the read error when one occurred.
    /// </summary>
    public event EventHandler<ShaRpcConnectionClosedEventArgs>? ConnectionClosed;

    /// <summary>
    /// Raised when a routed frame is dropped because the target facade cannot queue it.
    /// </summary>
    public event EventHandler<ShaRpcFrameDroppedEventArgs>? FrameDropped;

    /// <summary>
    /// Creates a generated proxy for a service exposed by the remote peer.
    /// </summary>
    public TService CreateProxy<TService>()
        where TService : class =>
        ShaRpcServiceRegistry.CreateProxy<TService>(_client);

    /// <summary>
    /// Alias for <see cref="CreateProxy{TService}"/>.
    /// </summary>
    public TService GetProxy<TService>()
        where TService : class =>
        CreateProxy<TService>();

    /// <summary>
    /// Registers an inbound service dispatcher on this peer.
    /// </summary>
    public void RegisterDispatcher(IServiceDispatcher dispatcher) =>
        _server.RegisterDispatcher(dispatcher);

    public static Task<ShaRpcPeer> StartAsync(
        IConnection connection,
        ISerializer serializer,
        Action<ShaRpcServerBuilder>? configureServer,
        TimeSpan? timeout,
        CancellationToken ct = default) =>
        StartAsync(
            connection,
            serializer,
            configureServer,
            new ShaRpcPeerOptions { RequestTimeout = timeout },
            ct);

    public static async Task<ShaRpcPeer> StartAsync(
        IConnection connection,
        ISerializer serializer,
        Action<ShaRpcServerBuilder>? configureServer = null,
        ShaRpcPeerOptions? options = null,
        CancellationToken ct = default)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (serializer is null)
        {
            throw new ArgumentNullException(nameof(serializer));
        }

        var effectiveOptions = options ?? new ShaRpcPeerOptions();
        var splitter = new DuplexConnectionSplitter(connection, effectiveOptions.ToSplitterOptions());

        var serverBuilder = new ShaRpcServerBuilder()
            .UseTransport(new SingleConnectionServerTransport(splitter.ServerConnection))
            .UseSerializer(serializer);
        configureServer?.Invoke(serverBuilder);
        var server = serverBuilder.Build();

        var clientBuilder = new ShaRpcClientBuilder()
            .UseTransport(new SingleConnectionTransport(splitter.ClientConnection))
            .UseSerializer(serializer);
        if (effectiveOptions.RequestTimeout is { } timeout)
        {
            clientBuilder.WithTimeout(timeout);
        }

        var client = clientBuilder.Build();
        var peer = new ShaRpcPeer(splitter, server, client);
        splitter.ReadError += peer.HandleReadError;
        splitter.Disconnected += peer.HandleDisconnected;
        splitter.ConnectionClosed += peer.HandleConnectionClosed;
        splitter.FrameDropped += peer.HandleFrameDropped;

        try
        {
            splitter.Start();
            await server.StartAsync(ct).ConfigureAwait(false);
            await client.ConnectAsync(ct).ConfigureAwait(false);
            return peer;
        }
        catch
        {
            await peer.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Closes the peer and its underlying connection. This operation is idempotent.
    /// </summary>
    public Task CloseAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return DisposeAsync().AsTask();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await SafeDisposeAsync(_client.DisposeAsync()).ConfigureAwait(false);
        await SafeDisposeAsync(_server.DisposeAsync()).ConfigureAwait(false);
        await _splitter.DisposeAsync().ConfigureAwait(false);
    }

    private void HandleReadError(Exception exception) => ReadError?.Invoke(exception);

    private void HandleDisconnected() => Disconnected?.Invoke();

    private void HandleConnectionClosed(object? sender, ShaRpcConnectionClosedEventArgs args) =>
        ConnectionClosed?.Invoke(this, args);

    private void HandleFrameDropped(object? sender, ShaRpcFrameDroppedEventArgs args) =>
        FrameDropped?.Invoke(this, args);

    private static async ValueTask SafeDisposeAsync(ValueTask operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch
        {
            // Best-effort shutdown.
        }
    }
}
