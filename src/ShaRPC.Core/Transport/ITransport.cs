namespace ShaRPC.Core.Transport;

/// <summary>
/// Represents a transport layer for establishing connections.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>
    /// Establishes a connection (client-side).
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the active connection (client-side).
    /// </summary>
    IRpcChannel? Connection { get; }

    /// <summary>
    /// Gets whether there is an active connection.
    /// </summary>
    bool IsConnected { get; }
}
