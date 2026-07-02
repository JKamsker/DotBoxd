namespace DotBoxD.Services.Attributes;

/// <summary>
/// Obsolete alias for <see cref="RpcServiceAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
[Obsolete("Use RpcServiceAttribute.")]
public sealed class DotBoxDServiceAttribute : RpcServiceAttribute;
