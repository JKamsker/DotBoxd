namespace SafeIR.Transport.Ipc;

using ShaRPC.Core;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.NamedPipes;

public static class SafeIrShaRpcMessagePackIpc
{
    private static readonly MessagePackRpcSerializer Serializer = new();
    private static readonly RpcPeerOptions DefaultClientOptions = new() {
        RequestTimeout = TimeSpan.FromSeconds(10),
        RejectInboundCalls = true
    };
    private static readonly RpcPeerOptions DefaultBidirectionalClientOptions = new() {
        RequestTimeout = TimeSpan.FromSeconds(10)
    };

    public static RpcHost Listen(
        IServerTransport transport,
        Action<RpcPeer> configurePeer,
        RpcPeerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(configurePeer);
        return RpcHost.Listen(transport, Serializer, options).ForEachPeer(configurePeer);
    }

    public static Task<RpcPeerSession> ConnectAsync(
        ITransport transport,
        RpcPeerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return transport.ConnectPeerAsync(Serializer, options ?? DefaultClientOptions, cancellationToken);
    }

    public static Task<RpcPeerSession> ConnectAsync(
        ITransport transport,
        Action<RpcPeer> configurePeer,
        RpcPeerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(configurePeer);
        return transport.ConnectPeerAsync(
            Serializer,
            configurePeer,
            options ?? DefaultBidirectionalClientOptions,
            cancellationToken);
    }

    public static RpcHost ListenNamedPipe(
        string pipeName,
        Action<RpcPeer> configurePeer,
        RpcPeerOptions? options = null)
        => Listen(new NamedPipeServerTransport(pipeName), configurePeer, options);

    public static Task<RpcPeerSession> ConnectNamedPipeAsync(
        string pipeName,
        RpcPeerOptions? options = null,
        CancellationToken cancellationToken = default)
        => ConnectNamedPipeAsync(".", pipeName, options, cancellationToken);

    public static Task<RpcPeerSession> ConnectNamedPipeAsync(
        string serverName,
        string pipeName,
        RpcPeerOptions? options = null,
        CancellationToken cancellationToken = default)
        => ConnectAsync(new NamedPipeClientTransport(serverName, pipeName), options, cancellationToken);
}
