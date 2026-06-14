namespace ShaRPC.Core.Generated;

/// <summary>
/// Describes a source-generated ShaRPC service and its generated implementation types.
/// </summary>
public readonly record struct ShaRpcGeneratedService(
    Type ServiceType,
    Type ProxyType,
    Type DispatcherType,
    string ServiceName)
{
    /// <summary>
    /// Describes the RPC-facing methods generated for this service.
    /// </summary>
    public IReadOnlyList<ShaRpcGeneratedMethod> Methods { get; init; } = Array.Empty<ShaRpcGeneratedMethod>();

    /// <summary>
    /// Creates service metadata with generated method descriptors.
    /// </summary>
    public ShaRpcGeneratedService(
        Type serviceType,
        Type proxyType,
        Type dispatcherType,
        string serviceName,
        IReadOnlyList<ShaRpcGeneratedMethod> methods)
        : this(serviceType, proxyType, dispatcherType, serviceName)
    {
        Methods = methods ?? throw new ArgumentNullException(nameof(methods));
    }
}
