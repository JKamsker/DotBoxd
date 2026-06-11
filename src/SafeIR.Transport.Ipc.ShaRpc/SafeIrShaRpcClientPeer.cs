namespace SafeIR.Transport.Ipc;

using ShaRPC.Core;
using ShaRPC.Transports.NamedPipes;

public sealed class SafeIrShaRpcClientPeer : IAsyncDisposable
{
    private readonly NamedPipeClientTransport _transport;

    internal SafeIrShaRpcClientPeer(NamedPipeClientTransport transport, RpcPeer peer)
    {
        _transport = transport;
        Peer = peer;
    }

    public RpcPeer Peer { get; }

    public async ValueTask DisposeAsync()
    {
        await Peer.DisposeAsync().ConfigureAwait(false);
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
