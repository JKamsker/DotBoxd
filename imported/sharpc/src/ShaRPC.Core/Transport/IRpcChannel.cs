using ShaRPC.Core.Buffers;

namespace ShaRPC.Core.Transport;

/// <summary>
/// A duplex, framed, bidirectional channel — the transport unit a <see cref="ShaRPC.Core.RpcPeer"/>
/// runs on. Responses flow back over the same channel, so it is always bidirectional even when the
/// call direction is one-way. Transports return this directly; implement it to add a custom transport.
/// </summary>
public interface IRpcChannel : IAsyncDisposable
{
    /// <summary>
    /// Sends a framed message over the channel.
    /// </summary>
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);

    /// <summary>
    /// Receives a framed message. The caller owns the returned <see cref="Payload"/> and must
    /// dispose it. A payload with <see cref="Payload.Length"/> of 0 signals the channel was closed.
    /// </summary>
    Task<Payload> ReceiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets whether the channel is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets a string representation of the remote endpoint.
    /// </summary>
    string RemoteEndpoint { get; }
}
