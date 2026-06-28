using System.Buffers;
using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Plugins.Runtime.Hooks;

internal static class RemoteLocalResultEncoder
{
    public static byte[] Encode<TResult>(TResult result)
        where TResult : struct, IHookResult
    {
        var members = Shape<TResult>.Members;
        using var writer = PooledRpcBufferWriter.Rent();
        if (members.Length == 0)
        {
            KernelRpcBinaryCodec.EncodeValue(
                KernelRpcMarshaller.ToSandboxValue(result, typeof(TResult)),
                writer);
        }
        else
        {
            KernelRpcBinaryCodec.BeginRecord(members.Length, writer);
            for (var i = 0; i < members.Length; i++)
            {
                members[i].WriteValue(result, writer);
            }
        }

        return writer.WrittenMemory.ToArray();
    }

    private static SandboxValue EncodeMember(object? value, Type type, string name)
    {
        if (value is not null)
        {
            return KernelRpcMarshaller.ToSandboxValue(value, type);
        }

        if (Nullable.GetUnderlyingType(type) is not null)
        {
            return KernelRpcMarshaller.ToSandboxValue(null, type);
        }

        return type == typeof(string) && string.Equals(name, "Reason", StringComparison.Ordinal)
            ? SandboxValue.FromString(string.Empty)
            : throw new NotSupportedException(
                $"Hook result field '{name}' of type '{type}' was null; the sandbox value model has no null.");
    }

    private static class Shape<TResult>
        where TResult : struct, IHookResult
    {
        public static readonly ResultMember<TResult>[] Members = BuildMembers<TResult>();
    }

    private static ResultMember<TResult>[] BuildMembers<TResult>()
        where TResult : struct, IHookResult
    {
        var type = typeof(TResult);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var members = new List<ResultMember<TResult>>();
        foreach (var property in type.GetProperties(flags))
        {
            if (property.CanRead &&
                property.GetIndexParameters().Length == 0 &&
                !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal) &&
                !KernelRpcMarshaller.IsIgnoredMember(property))
            {
                members.Add(new ResultMember<TResult>(
                    property.Name,
                    CreateWriter<TResult>(property, property.PropertyType, property.Name),
                    property.MetadataToken));
            }
        }

        if (members.Count == 0)
        {
            foreach (var field in type.GetFields(flags))
            {
                if (!KernelRpcMarshaller.IsIgnoredMember(field))
                {
                    members.Add(new ResultMember<TResult>(
                        field.Name,
                        CreateWriter<TResult>(field, field.FieldType, field.Name),
                        field.MetadataToken));
                }
            }
        }

        members.Sort(static (left, right) => left.Order.CompareTo(right.Order));
        return [.. members];
    }

    private static Action<TResult, IBufferWriter<byte>> CreateWriter<TResult>(
        MemberInfo member,
        Type type,
        string name)
        where TResult : struct, IHookResult
    {
        if (type == typeof(bool))
        {
            var getter = CreateGetter<TResult, bool>(member);
            return (result, writer) => KernelRpcBinaryCodec.EncodeBoolValue(getter(result), writer);
        }

        if (type == typeof(int))
        {
            var getter = CreateGetter<TResult, int>(member);
            return (result, writer) => KernelRpcBinaryCodec.EncodeInt32Value(getter(result), writer);
        }

        if (type == typeof(long))
        {
            var getter = CreateGetter<TResult, long>(member);
            return (result, writer) => KernelRpcBinaryCodec.EncodeInt64Value(getter(result), writer);
        }

        if (type == typeof(float))
        {
            var getter = CreateGetter<TResult, float>(member);
            return (result, writer) => KernelRpcBinaryCodec.EncodeDoubleValue(getter(result), writer);
        }

        if (type == typeof(double))
        {
            var getter = CreateGetter<TResult, double>(member);
            return (result, writer) => KernelRpcBinaryCodec.EncodeDoubleValue(getter(result), writer);
        }

        if (type == typeof(string))
        {
            var getter = CreateGetter<TResult, string?>(member);
            return (result, writer) => EncodeStringMember(getter(result), name, writer);
        }

        if (type == typeof(Guid))
        {
            var getter = CreateGetter<TResult, Guid>(member);
            return (result, writer) => KernelRpcBinaryCodec.EncodeGuidValue(getter(result), writer);
        }

        var objectGetter = CreateGetter<TResult, object?>(member);
        return (result, writer) => KernelRpcBinaryCodec.EncodeRecordField(
            EncodeMember(objectGetter(result), type, name),
            writer);
    }

    private static void EncodeStringMember(string? value, string name, IBufferWriter<byte> writer)
    {
        if (value is not null)
        {
            KernelRpcBinaryCodec.EncodeStringValue(value, writer);
            return;
        }

        if (string.Equals(name, "Reason", StringComparison.Ordinal))
        {
            KernelRpcBinaryCodec.EncodeStringValue(string.Empty, writer);
            return;
        }

        throw new NotSupportedException(
            $"Hook result field '{name}' of type '{typeof(string)}' was null; the sandbox value model has no null.");
    }

    private static Func<TResult, TValue> CreateGetter<TResult, TValue>(MemberInfo member)
        where TResult : struct, IHookResult
    {
        var result = LinqExpression.Parameter(typeof(TResult), "result");
        var access = member switch
        {
            PropertyInfo property => LinqExpression.Property(result, property),
            FieldInfo field => LinqExpression.Field(result, field),
            _ => throw new NotSupportedException($"Hook result member '{member.Name}' is not supported.")
        };
        return LinqExpression.Lambda<Func<TResult, TValue>>(
                LinqExpression.Convert(access, typeof(TValue)),
                result)
            .Compile();
    }

    private sealed record ResultMember<TResult>(
        string Name,
        Action<TResult, IBufferWriter<byte>> WriteValue,
        int Order);
}
