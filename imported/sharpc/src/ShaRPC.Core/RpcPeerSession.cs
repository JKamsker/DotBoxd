using System.Runtime.ExceptionServices;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

/// <summary>
/// Owns a connected transport and the <see cref="RpcPeer"/> running over it.
/// Dispose the session to close both in the correct order.
/// </summary>
public sealed class RpcPeerSession : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly object _disposeLock = new();
    private Task? _disposeTask;

    private RpcPeerSession(ITransport transport, RpcPeer peer)
    {
        _transport = transport;
        Peer = peer;
    }

    public RpcPeer Peer { get; }

    public bool IsConnected => Peer.IsConnected && _transport.IsConnected;

    public string RemoteEndpoint => Peer.RemoteEndpoint;

    public TService Get<TService>()
        where TService : class =>
        Peer.Get<TService>();

    public static Task<RpcPeerSession> ConnectAsync(
        ITransport transport,
        ISerializer serializer,
        RpcPeerOptions? options = null,
        CancellationToken ct = default) =>
        ConnectCoreAsync(transport, serializer, null, options, ct);

    public static Task<RpcPeerSession> ConnectAsync(
        ITransport transport,
        ISerializer serializer,
        Action<RpcPeer> configurePeer,
        RpcPeerOptions? options = null,
        CancellationToken ct = default)
    {
        if (configurePeer is null)
        {
            throw new ArgumentNullException(nameof(configurePeer));
        }

        return ConnectCoreAsync(transport, serializer, configurePeer, options, ct);
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private static async Task<RpcPeerSession> ConnectCoreAsync(
        ITransport transport,
        ISerializer serializer,
        Action<RpcPeer>? configurePeer,
        RpcPeerOptions? options,
        CancellationToken ct)
    {
        if (transport is null)
        {
            throw new ArgumentNullException(nameof(transport));
        }

        if (serializer is null)
        {
            throw new ArgumentNullException(nameof(serializer));
        }

        RpcPeer? peer = null;
        try
        {
            await transport.ConnectAsync(ct).ConfigureAwait(false);
            var channel = transport.Connection ??
                throw new InvalidOperationException("Transport connected without publishing an RPC channel.");
            peer = RpcPeer.Over(channel, serializer, options);
            configurePeer?.Invoke(peer);
            peer.Start();
            return new RpcPeerSession(transport, peer);
        }
        catch
        {
            await DisposeFailedConnectAsync(peer, transport).ConfigureAwait(false);
            throw;
        }
    }

    private async Task DisposeCoreAsync()
    {
        Exception? first = null;
        try
        {
            await Peer.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            first = ex;
        }

        try
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (first is null)
            {
                first = ex;
            }
            else
            {
                RpcDiagnostics.Report("Transport dispose during peer session teardown failed", ex);
            }
        }

        if (first is not null)
        {
            ExceptionDispatchInfo.Capture(first).Throw();
        }
    }

    private static async Task DisposeFailedConnectAsync(RpcPeer? peer, ITransport transport)
    {
        if (peer is not null)
        {
            try
            {
                await peer.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RpcDiagnostics.Report("Peer dispose after failed transport connect failed", ex);
            }
        }

        try
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Transport dispose after failed peer connect failed", ex);
        }
    }
}
