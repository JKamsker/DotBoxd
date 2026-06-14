namespace DotBoxd.Services.Transport;

/// <summary>
/// Represents a server-side transport that accepts incoming connections.
/// </summary>
public interface IServerTransport : IAsyncDisposable
{
    /// <summary>
    /// Starts listening for connections.
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Accepts an incoming connection.
    /// </summary>
    Task<IRpcChannel> AcceptAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops listening for connections.
    /// </summary>
    Task StopAsync(CancellationToken ct = default);
}
