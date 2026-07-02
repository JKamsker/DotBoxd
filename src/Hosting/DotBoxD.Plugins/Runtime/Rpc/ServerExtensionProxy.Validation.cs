using System.Reflection;

namespace DotBoxD.Plugins.Runtime.Rpc;

internal static class ServerExtensionProxyValidation
{
    public static void ValidatePayloadType(Type type)
    {
        if (IsTaskLike(type))
        {
            throw new NotSupportedException(
                $"Server extension proxy task-like payload type '{type}' is not supported; " +
                "Task and ValueTask are only supported as top-level return types.");
        }

        KernelRpcMarshaller.RejectUnsupportedNullableValueTypesForServerExtension(type);
        _ = KernelRpcMarshaller.SandboxTypeOf(type);
    }

    public static void RejectNullReferenceDefault(ParameterInfo parameter)
    {
        if (parameter.HasDefaultValue &&
            parameter.DefaultValue is null &&
            !parameter.ParameterType.IsValueType)
        {
            throw new NotSupportedException(
                $"Server extension service parameter '{parameter.Name}' cannot default to null because kernel RPC does not encode null reference values.");
        }
    }

    private static bool IsTaskLike(Type type)
    {
        if (type == typeof(Task) || type == typeof(ValueTask))
        {
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(Task<>) || definition == typeof(ValueTask<>);
    }
}
