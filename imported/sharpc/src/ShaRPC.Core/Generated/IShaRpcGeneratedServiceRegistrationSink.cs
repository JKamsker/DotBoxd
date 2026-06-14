using ShaRPC.Core.Server;

namespace ShaRPC.Core.Generated;

/// <summary>
/// Receives source-generated service, proxy, and dispatcher registrations without scanning generated types.
/// </summary>
public interface IShaRpcGeneratedServiceRegistrationSink
{
    /// <summary>
    /// Adds one generated proxy and dispatcher pair for a ShaRPC service interface.
    /// </summary>
    void AddService<TService, TProxy, TDispatcher>()
        where TService : class
        where TProxy : TService
        where TDispatcher : IServiceDispatcher;
}

/// <summary>
/// Compatibility spelling for callers that use the project acronym casing.
/// </summary>
public interface IShaRPCGeneratedServiceRegistrationSink : IShaRpcGeneratedServiceRegistrationSink
{
}
