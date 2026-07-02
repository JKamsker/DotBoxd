namespace DotBoxD.Services.Attributes;

/// <summary>
/// Marks an interface as an RPC service. The source generator creates client proxy and server dispatcher
/// implementations for this interface.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public class RpcServiceAttribute : Attribute
{
    /// <summary>
    /// Optional custom service name. If not specified, the interface name is used.
    /// </summary>
    public string? Name { get; set; }
}
