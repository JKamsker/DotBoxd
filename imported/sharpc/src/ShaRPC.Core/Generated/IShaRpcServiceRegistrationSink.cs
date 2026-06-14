namespace ShaRPC.Core.Generated;

/// <summary>
/// Receives source-generated service registrations without scanning generated types.
/// </summary>
public interface IShaRpcServiceRegistrationSink
{
    /// <summary>
    /// Adds one generated proxy implementation for a ShaRPC service interface.
    /// </summary>
    void AddService<TService, TImplementation>()
        where TService : class
        where TImplementation : TService;
}

/// <summary>
/// Compatibility spelling for callers that use the project acronym casing.
/// </summary>
public interface IShaRPCServiceRegistrationSink : IShaRpcServiceRegistrationSink
{
}
