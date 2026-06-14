using DotBoxd.Services.Server;

namespace DotBoxd.Services.Generated;

/// <summary>
/// Receives source-generated service, proxy, and dispatcher registrations without scanning generated types.
/// </summary>
public interface IDotBoxdGeneratedServiceRegistrationSink
{
    /// <summary>
    /// Adds one generated proxy and dispatcher pair for a DotBoxd service interface.
    /// </summary>
    void AddService<TService, TProxy, TDispatcher>()
        where TService : class
        where TProxy : TService
        where TDispatcher : IServiceDispatcher;
}
