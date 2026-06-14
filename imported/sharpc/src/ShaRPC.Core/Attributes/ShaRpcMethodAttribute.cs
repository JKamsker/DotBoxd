namespace ShaRPC.Core.Attributes;

/// <summary>
/// Marks a method as a ShaRPC endpoint. This attribute is optional -
/// all methods in a [ShaRpcService] interface are included by default.
/// Use this attribute to customize method behavior.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ShaRpcMethodAttribute : Attribute
{
    /// <summary>
    /// Optional custom method name. If not specified, the method name is used.
    /// </summary>
    public string? Name { get; set; }
}
