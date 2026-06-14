namespace DotBoxd.Services.Generated;

/// <summary>
/// Describes a source-generated DotBoxd service and its generated implementation types.
/// </summary>
public readonly record struct DotBoxdGeneratedService(
    Type ServiceType,
    Type ProxyType,
    Type DispatcherType,
    string ServiceName)
{
    /// <summary>
    /// Describes the RPC-facing methods generated for this service.
    /// </summary>
    public IReadOnlyList<DotBoxdGeneratedMethod> Methods { get; init; } = Array.Empty<DotBoxdGeneratedMethod>();

    /// <summary>
    /// Creates service metadata with generated method descriptors.
    /// </summary>
    public DotBoxdGeneratedService(
        Type serviceType,
        Type proxyType,
        Type dispatcherType,
        string serviceName,
        IReadOnlyList<DotBoxdGeneratedMethod> methods)
        : this(serviceType, proxyType, dispatcherType, serviceName)
    {
        Methods = methods ?? throw new ArgumentNullException(nameof(methods));
    }
}
