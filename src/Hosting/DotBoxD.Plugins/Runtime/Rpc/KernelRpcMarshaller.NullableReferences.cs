using System.Reflection;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static void RejectNullableReferenceDtoShape(Type dtoType, RecordShape shape)
    {
        foreach (var field in shape.Fields)
        {
            RejectNullableReferenceDtoMember(dtoType, field.Member);
        }
    }

    private static void RejectNullableReferenceDtoMember(Type dtoType, MemberInfo member)
    {
        var nullability = new NullabilityInfoContext();
        var info = member switch
        {
            PropertyInfo property => nullability.Create(property),
            FieldInfo field => nullability.Create(field),
            _ => null,
        };

        if (info is not null && ContainsNullableReference(info))
        {
            throw new NotSupportedException(
                $"Server extension DTO '{dtoType}' member '{member.Name}' is a nullable reference type; " +
                "kernel RPC does not encode null reference values.");
        }
    }

    private static bool ContainsNullableReference(NullabilityInfo info)
    {
        if (!info.Type.IsValueType && info.ReadState == NullabilityState.Nullable)
        {
            return true;
        }

        if (info.ElementType is not null && ContainsNullableReference(info.ElementType))
        {
            return true;
        }

        foreach (var argument in info.GenericTypeArguments)
        {
            if (ContainsNullableReference(argument))
            {
                return true;
            }
        }

        return false;
    }
}
