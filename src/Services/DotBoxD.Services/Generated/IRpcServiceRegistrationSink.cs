namespace DotBoxD.Services.Generated;

/// <summary>
/// Receives source-generated service registrations without scanning generated types.
/// </summary>
public interface IRpcServiceRegistrationSink
{
    /// <summary>
    /// Adds one generated proxy implementation for an RPC service interface.
    /// </summary>
    void AddService<TService, TImplementation>()
        where TService : class
        where TImplementation : TService;
}
