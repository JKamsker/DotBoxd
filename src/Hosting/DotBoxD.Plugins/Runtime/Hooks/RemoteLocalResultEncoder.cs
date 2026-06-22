using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins.Runtime.Hooks;

internal static class RemoteLocalResultEncoder
{
    public static byte[] Encode<TResult>(TResult result)
        where TResult : struct, IHookResult
    {
        var value = Shape<TResult>.Members.Length == 0
            ? KernelRpcMarshaller.ToSandboxValue(result, typeof(TResult))
            : EncodeRecord(result);
        return KernelRpcBinaryCodec.EncodeValue(value);
    }

    private static SandboxValue EncodeRecord<TResult>(TResult result)
    {
        var members = Shape<TResult>.Members;
        var values = new SandboxValue[members.Length];
        object boxed = result!;
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            values[i] = EncodeMember(member.GetValue(boxed), member.Type, member.Name);
        }

        return SandboxValue.FromOwnedRecord(values);
    }

    private static SandboxValue EncodeMember(object? value, Type type, string name)
    {
        if (value is not null)
        {
            return KernelRpcMarshaller.ToSandboxValue(value, type);
        }

        return type == typeof(string) && string.Equals(name, "Reason", StringComparison.Ordinal)
            ? SandboxValue.FromString(string.Empty)
            : throw new NotSupportedException(
                $"Hook result field '{name}' of type '{type}' was null; the sandbox value model has no null.");
    }

    private static class Shape<TResult>
    {
        public static readonly ResultMember[] Members = BuildMembers(typeof(TResult));
    }

    private static ResultMember[] BuildMembers(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var members = new List<ResultMember>();
        foreach (var property in type.GetProperties(flags))
        {
            if (property.CanRead &&
                property.GetIndexParameters().Length == 0 &&
                !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal) &&
                !KernelRpcMarshaller.IsIgnoredMember(property))
            {
                members.Add(new ResultMember(property.Name, property.PropertyType, property.GetValue, property.MetadataToken));
            }
        }

        if (members.Count == 0)
        {
            foreach (var field in type.GetFields(flags))
            {
                if (!KernelRpcMarshaller.IsIgnoredMember(field))
                {
                    members.Add(new ResultMember(field.Name, field.FieldType, field.GetValue, field.MetadataToken));
                }
            }
        }

        members.Sort(static (left, right) => left.Order.CompareTo(right.Order));
        return [.. members];
    }

    private sealed record ResultMember(
        string Name,
        Type Type,
        Func<object, object?> GetValue,
        int Order);
}
