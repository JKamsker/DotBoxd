namespace ShaRPC.Core.Attributes;

/// <summary>
/// Marks an interface as a ShaRPC service. The source generator will create
/// client proxy and server dispatcher implementations for this interface.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class ShaRpcServiceAttribute : Attribute
{
    /// <summary>
    /// Optional custom service name. If not specified, the interface name is used.
    /// </summary>
    public string? Name { get; set; }
}
