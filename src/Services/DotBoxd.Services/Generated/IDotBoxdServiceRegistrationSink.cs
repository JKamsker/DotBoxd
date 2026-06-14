namespace DotBoxd.Services.Generated;

/// <summary>
/// Receives source-generated service registrations without scanning generated types.
/// </summary>
public interface IDotBoxdServiceRegistrationSink
{
    /// <summary>
    /// Adds one generated proxy implementation for a DotBoxd service interface.
    /// </summary>
    void AddService<TService, TImplementation>()
        where TService : class
        where TImplementation : TService;
}
