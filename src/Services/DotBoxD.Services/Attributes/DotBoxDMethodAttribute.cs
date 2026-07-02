namespace DotBoxD.Services.Attributes;

/// <summary>
/// Obsolete alias for <see cref="RpcMethodAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
[Obsolete("Use RpcMethodAttribute.")]
public sealed class DotBoxDMethodAttribute : RpcMethodAttribute;
