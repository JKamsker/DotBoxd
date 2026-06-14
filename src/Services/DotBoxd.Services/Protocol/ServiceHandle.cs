namespace DotBoxd.Services.Protocol;

/// <summary>
/// Wire payload returned by a service method whose declared return type is itself
/// a <c>[DotBoxdService]</c> interface. Carries the opaque instance identifier the
/// server allocated when the root method ran; the client wraps the handle in a
/// generated sub-service proxy that uses the identifier on every subsequent call.
/// </summary>
public struct ServiceHandle
{
    /// <summary>
    /// Initializes an empty handle. Required so the string field initializers run for
    /// <c>new ServiceHandle()</c> and during deserialization.
    /// </summary>
    public ServiceHandle()
    {
    }

    /// <summary>The RPC service name of the sub-service (e.g. <c>"ISubService"</c>).</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Opaque, per-connection token issued by the server's instance registry. Treat as
    /// a black box on the client; it is not a security boundary on its own (combined with
    /// per-connection scoping, the registry does not let one connection reach another's
    /// instances).
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;
}
