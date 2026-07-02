namespace DotBoxD.Services.Attributes;

/// <summary>
/// Marks a method as an RPC endpoint. This attribute is optional: all methods in an
/// <see cref="RpcServiceAttribute"/> interface are included by default.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class RpcMethodAttribute : Attribute
{
    /// <summary>
    /// Optional custom method name. If not specified, the method name is used.
    /// </summary>
    public string? Name { get; set; }
}
