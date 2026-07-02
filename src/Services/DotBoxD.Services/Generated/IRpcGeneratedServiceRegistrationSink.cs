using DotBoxD.Services.Server;

namespace DotBoxD.Services.Generated;

/// <summary>
/// Receives source-generated service, proxy, and dispatcher registrations without scanning generated types.
/// </summary>
public interface IRpcGeneratedServiceRegistrationSink
{
    /// <summary>
    /// Adds one generated proxy and dispatcher pair for an RPC service interface.
    /// </summary>
    void AddService<TService, TProxy, TDispatcher>()
        where TService : class
        where TProxy : TService
        where TDispatcher : IServiceDispatcher;
}
