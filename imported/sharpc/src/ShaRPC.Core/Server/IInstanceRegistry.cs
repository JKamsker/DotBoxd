namespace ShaRPC.Core.Server;

/// <summary>
/// Per-connection registry that holds server-side sub-service instances by opaque
/// instance identifier. Created by the server when a connection is accepted and
/// drained when the connection closes — instances therefore have connection-scoped
/// lifetime and cannot leak across tenants.
/// </summary>
public interface IInstanceRegistry
{
    /// <summary>
    /// Registers an instance under <paramref name="serviceName"/> and returns the
    /// freshly allocated identifier the client will quote on subsequent calls.
    /// </summary>
    string Register(string serviceName, object instance);

    /// <summary>
    /// Looks up an instance previously registered under
    /// (<paramref name="serviceName"/>, <paramref name="instanceId"/>).
    /// </summary>
    bool TryGet(string serviceName, string instanceId, out object instance);

    /// <summary>
    /// Releases an instance early (the connection-teardown path also clears it).
    /// </summary>
    void Release(string serviceName, string instanceId);

    /// <summary>Removes every entry — called from the connection-cleanup path.</summary>
    void ReleaseAll();
}
