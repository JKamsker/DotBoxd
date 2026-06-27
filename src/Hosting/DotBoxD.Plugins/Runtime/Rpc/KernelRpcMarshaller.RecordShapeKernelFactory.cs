using System.Linq.Expressions;
using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static object? DefaultParameterValue(ParameterInfo parameter)
    {
        var value = parameter.DefaultValue;
        return value is DBNull or Missing
            ? (parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null)
            : value;
    }

    private static class RecordShapeKernelFactory
    {
        public static Func<KernelRpcValue, object>? Create(
            ConstructorInfo? constructor,
            IReadOnlyList<int> constructorMap,
            IReadOnlyList<RecordMember> fields)
        {
            if (constructor is null)
            {
                return null;
            }

            var value = LinqExpression.Parameter(typeof(KernelRpcValue), "value");
            var parameters = constructor.GetParameters();
            var arguments = new LinqExpression[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var fieldIndex = constructorMap[i];
                if (fieldIndex < 0)
                {
                    arguments[i] = LinqExpression.Constant(
                        DefaultParameterValue(parameters[i]),
                        parameters[i].ParameterType);
                    continue;
                }

                var kernelField = LinqExpression.Call(
                    value,
                    nameof(KernelRpcValue.GetItem),
                    Type.EmptyTypes,
                    LinqExpression.Constant(fieldIndex));
                arguments[i] = LinqExpression.Convert(
                    ReadKernelRecordField(kernelField, fields[fieldIndex].Type),
                    parameters[i].ParameterType);
            }

            var created = LinqExpression.New(constructor, arguments);
            if (RecordTailBindings(
                    constructorMap,
                    fields,
                    fieldIndex => LinqExpression.Call(
                        value,
                        nameof(KernelRpcValue.GetItem),
                        Type.EmptyTypes,
                        LinqExpression.Constant(fieldIndex)),
                    ReadKernelRecordField) is not { } bindings)
            {
                return null;
            }

            var initialized = bindings.Count == 0
                ? (LinqExpression)created
                : LinqExpression.MemberInit(created, bindings);
            var body = LinqExpression.Convert(initialized, typeof(object));
            return LinqExpression.Lambda<Func<KernelRpcValue, object>>(body, value).Compile();
        }

    }

    private static class RecordShapeSetterFactory
    {
        public static Func<RecordValue, object>? CreateSandbox(Type type, IReadOnlyList<RecordMember> fields)
        {
            if (!CanBindSetters(fields) || CreateNewExpression(type) is not { } newInstance)
            {
                return null;
            }

            var record = LinqExpression.Parameter(typeof(RecordValue), "record");
            var recordFields = LinqExpression.Property(record, nameof(RecordValue.Fields));
            var body = LinqExpression.Convert(
                LinqExpression.MemberInit(newInstance, SandboxBindings(recordFields, fields)),
                typeof(object));
            return LinqExpression.Lambda<Func<RecordValue, object>>(body, record).Compile();
        }

        public static Func<KernelRpcValue, object>? CreateKernel(Type type, IReadOnlyList<RecordMember> fields)
        {
            if (!CanBindSetters(fields) || CreateNewExpression(type) is not { } newInstance)
            {
                return null;
            }

            var value = LinqExpression.Parameter(typeof(KernelRpcValue), "value");
            var body = LinqExpression.Convert(
                LinqExpression.MemberInit(newInstance, KernelBindings(value, fields)),
                typeof(object));
            return LinqExpression.Lambda<Func<KernelRpcValue, object>>(body, value).Compile();
        }

        private static IEnumerable<MemberBinding> SandboxBindings(
            LinqExpression recordFields,
            IReadOnlyList<RecordMember> fields)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var sandboxField = LinqExpression.Property(recordFields, "Item", LinqExpression.Constant(i));
                yield return LinqExpression.Bind(
                    field.Member,
                    LinqExpression.Convert(ReadSandboxRecordField(sandboxField, field.Type), field.Type));
            }
        }

        private static IEnumerable<MemberBinding> KernelBindings(
            LinqExpression value,
            IReadOnlyList<RecordMember> fields)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var kernelField = LinqExpression.Call(
                    value,
                    nameof(KernelRpcValue.GetItem),
                    Type.EmptyTypes,
                    LinqExpression.Constant(i));
                yield return LinqExpression.Bind(
                    field.Member,
                    LinqExpression.Convert(ReadKernelRecordField(kernelField, field.Type), field.Type));
            }
        }

        private static bool CanBindSetters(IReadOnlyList<RecordMember> fields)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                if (fields[i].Member is PropertyInfo property)
                {
                    if (property.SetMethod is not { IsPublic: true })
                    {
                        return false;
                    }
                }
                else if (fields[i].Member is FieldInfo field)
                {
                    if (field.IsInitOnly)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        private static NewExpression? CreateNewExpression(Type type)
        {
            if (type.IsValueType)
            {
                return LinqExpression.New(type);
            }

            var constructor = type.GetConstructor(Type.EmptyTypes);
            return constructor is null ? null : LinqExpression.New(constructor);
        }
    }

    private static readonly MethodInfo FromSandboxValueMethod =
        typeof(KernelRpcMarshaller).GetMethod(
            nameof(FromSandboxValue),
            BindingFlags.Public | BindingFlags.Static,
            null,
            [typeof(SandboxValue), typeof(Type)],
            null)!;
    private static readonly MethodInfo FromKernelRpcValueMethod =
        typeof(KernelRpcMarshaller).GetMethod(
            nameof(FromKernelRpcValue),
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            [typeof(KernelRpcValue), typeof(Type)],
            null)!;
    private static readonly MethodInfo ReadBoolMethod = ScalarReader(nameof(ReadBool));
    private static readonly MethodInfo ReadInt32Method = ScalarReader(nameof(ReadInt32));
    private static readonly MethodInfo ReadInt64Method = ScalarReader(nameof(ReadInt64));
    private static readonly MethodInfo ReadDoubleMethod = ScalarReader(nameof(ReadDouble));
    private static readonly MethodInfo ReadFloatMethod = ScalarReader(nameof(ReadFloat));
    private static readonly MethodInfo ReadStringMethod = ScalarReader(nameof(ReadString));
    private static readonly MethodInfo ReadGuidMethod = ScalarReader(nameof(ReadGuid));
    private static readonly MethodInfo DoubleToSingleMethod =
        MarshallerMethod(nameof(DoubleToSingle), typeof(double));
    private static readonly MethodInfo EnumFromInt32Method =
        MarshallerMethod(nameof(EnumFromInt32), typeof(Type), typeof(int));
    private static readonly MethodInfo EnumFromInt64Method =
        MarshallerMethod(nameof(EnumFromInt64), typeof(Type), typeof(long));

    private static LinqExpression ReadSandboxRecordField(LinqExpression sandboxField, Type fieldType)
    {
        if (fieldType == typeof(bool))
            return LinqExpression.Call(ReadBoolMethod, sandboxField);
        if (fieldType == typeof(int))
            return LinqExpression.Call(ReadInt32Method, sandboxField);
        if (fieldType == typeof(long))
            return LinqExpression.Call(ReadInt64Method, sandboxField);
        if (fieldType == typeof(double))
            return LinqExpression.Call(ReadDoubleMethod, sandboxField);
        if (fieldType == typeof(float))
            return LinqExpression.Call(ReadFloatMethod, sandboxField);
        if (fieldType == typeof(string))
            return LinqExpression.Call(ReadStringMethod, sandboxField);
        if (fieldType == typeof(Guid))
            return LinqExpression.Call(ReadGuidMethod, sandboxField);

        return LinqExpression.Call(
            FromSandboxValueMethod,
            sandboxField,
            LinqExpression.Constant(fieldType, typeof(Type)));
    }

    private static LinqExpression ReadKernelRecordField(LinqExpression kernelField, Type fieldType)
    {
        if (fieldType == typeof(bool))
            return LinqExpression.Property(kernelField, nameof(KernelRpcValue.BoolValue));
        if (fieldType == typeof(int))
            return LinqExpression.Property(kernelField, nameof(KernelRpcValue.Int32Value));
        if (fieldType == typeof(long))
            return LinqExpression.Property(kernelField, nameof(KernelRpcValue.Int64Value));
        if (fieldType == typeof(float))
        {
            return LinqExpression.Call(
                DoubleToSingleMethod,
                LinqExpression.Property(kernelField, nameof(KernelRpcValue.DoubleValue)));
        }
        if (fieldType == typeof(double))
            return LinqExpression.Property(kernelField, nameof(KernelRpcValue.DoubleValue));
        if (fieldType == typeof(string))
            return LinqExpression.Property(kernelField, nameof(KernelRpcValue.TextValue));
        if (fieldType == typeof(Guid))
            return LinqExpression.Property(kernelField, nameof(KernelRpcValue.GuidValue));
        if (fieldType.IsEnum)
        {
            var propertyName = EnumUsesI64(fieldType) ? nameof(KernelRpcValue.Int64Value) : nameof(KernelRpcValue.Int32Value);
            var method = EnumUsesI64(fieldType) ? EnumFromInt64Method : EnumFromInt32Method;
            return LinqExpression.Convert(
                LinqExpression.Call(
                    method,
                    LinqExpression.Constant(fieldType, typeof(Type)),
                    LinqExpression.Property(kernelField, propertyName)),
                fieldType);
        }

        return LinqExpression.Call(
            FromKernelRpcValueMethod,
            kernelField,
            LinqExpression.Constant(fieldType, typeof(Type)));
    }

    private static MethodInfo ScalarReader(string name)
        => typeof(KernelRpcMarshaller).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private static MethodInfo MarshallerMethod(string name, params Type[] parameterTypes)
        => typeof(KernelRpcMarshaller).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            parameterTypes,
            null)!;

    private static bool ReadBool(SandboxValue value)
        => value is BoolValue typed ? typed.Value : throw CannotMarshalScalar(value, typeof(bool));

    private static int ReadInt32(SandboxValue value)
        => value is I32Value typed ? typed.Value : throw CannotMarshalScalar(value, typeof(int));

    private static long ReadInt64(SandboxValue value)
        => value is I64Value typed ? typed.Value : throw CannotMarshalScalar(value, typeof(long));

    private static double ReadDouble(SandboxValue value)
        => value is F64Value typed ? typed.Value : throw CannotMarshalScalar(value, typeof(double));

    private static float ReadFloat(SandboxValue value)
        => value is F64Value typed ? DoubleToSingle(typed.Value) : throw CannotMarshalScalar(value, typeof(float));

    private static string ReadString(SandboxValue value)
        => value is StringValue typed ? typed.Value : throw CannotMarshalScalar(value, typeof(string));

    private static Guid ReadGuid(SandboxValue value)
        => value is GuidValue typed ? typed.Value : throw CannotMarshalScalar(value, typeof(Guid));

    private static NotSupportedException CannotMarshalScalar(SandboxValue value, Type type)
        => new($"Server extension cannot marshal sandbox value '{value.Type}' to type '{type}'.");
}
