namespace ShaRPC.Core.Server;

/// <summary>
/// Marker for dispatchers whose methods cannot consume streamed arguments or produce streamed
/// responses. The peer can use this to skip allocating a streaming context for ordinary RPC calls.
/// </summary>
public interface INonStreamingServiceDispatcher
{
}
