using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private readonly record struct RecordMember(MemberInfo Member, Type Type)
    {
        public string Name => Member.Name;

        // A computed/get-only property (e.g. `Map => new(Channel, MapId)`) has no set accessor: it is derived
        // from other members and cannot — and must not — be assigned when reconstructing the record. Init-only
        // properties do expose a set accessor, so reflection can still assign them.
        public bool IsSettable => Member switch
        {
            PropertyInfo property => property.SetMethod is not null,
            FieldInfo fieldInfo => !fieldInfo.IsInitOnly,
            _ => false,
        };

        public static RecordMember FromProperty(PropertyInfo property) => new(property, property.PropertyType);

        public static RecordMember FromField(FieldInfo field) => new(field, field.FieldType);

        public object? GetValue(object instance)
            => Member is PropertyInfo property ? property.GetValue(instance) : ((FieldInfo)Member).GetValue(instance);

        public void SetValue(object instance, object? value)
        {
            if (Member is PropertyInfo property)
            {
                property.SetValue(instance, value);
            }
            else
            {
                ((FieldInfo)Member).SetValue(instance, value);
            }
        }
    }

    private sealed class RecordShape
    {
        private readonly ConstructorInfo? _constructor;
        private readonly int[] _constructorMap;
        private readonly Func<object, object?>[] _getters;
        private readonly Func<KernelRpcValue, object>? _kernelFactory;
        private readonly Func<RecordValue, object>? _recordFactory;
        private readonly Type _type;

        public RecordShape(Type type, RecordMember[] fields)
        {
            _type = type;
            Fields = fields;
            _getters = CreateGetters(fields);
            (_constructor, _constructorMap) = FindConstructor(type, fields);
            _recordFactory = CreateRecordFactory(_constructor, _constructorMap, fields) ??
                RecordShapeSetterFactory.CreateSandbox(type, fields);
            _kernelFactory = RecordShapeKernelFactory.Create(_constructor, _constructorMap, fields) ??
                RecordShapeSetterFactory.CreateKernel(type, fields);
        }

        public IReadOnlyList<RecordMember> Fields { get; }

        public object? GetValue(object instance, int index)
            => _getters[index](instance);

        public object Construct(RecordValue record)
        {
            if (_recordFactory is not null)
            {
                return _recordFactory(record);
            }

            var arguments = new object?[Fields.Count];
            for (var i = 0; i < Fields.Count; i++)
            {
                arguments[i] = FromSandboxValue(record.Fields[i], Fields[i].Type);
            }

            return ConstructFromArguments(arguments);
        }

        public object Construct(KernelRpcValue value)
        {
            if (_kernelFactory is not null)
            {
                return _kernelFactory(value);
            }

            var arguments = new object?[Fields.Count];
            for (var i = 0; i < Fields.Count; i++)
            {
                arguments[i] = FromKernelRpcValue(value.GetItem(i), Fields[i].Type);
            }

            return ConstructFromArguments(arguments);
        }

        private object ConstructFromArguments(object?[] arguments)
        {
            var instance = Activator.CreateInstance(_type)
                ?? throw new NotSupportedException($"Server extension could not construct '{_type}'.");
            for (var i = 0; i < Fields.Count; i++)
            {
                // Skip derived/get-only members: they have no set accessor and are recomputed from the members
                // that were assigned, so assigning them is both impossible and unnecessary.
                if (Fields[i].IsSettable)
                {
                    Fields[i].SetValue(instance, arguments[i]);
                }
            }

            return instance;
        }

        private static Func<RecordValue, object>? CreateRecordFactory(
            ConstructorInfo? constructor,
            IReadOnlyList<int> constructorMap,
            IReadOnlyList<RecordMember> fields)
        {
            if (constructor is null)
            {
                return null;
            }

            var record = LinqExpression.Parameter(typeof(RecordValue), "record");
            var recordFields = LinqExpression.Property(record, nameof(RecordValue.Fields));
            var parameters = constructor.GetParameters();
            var arguments = new LinqExpression[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var fieldIndex = constructorMap[i];
                var sandboxField = LinqExpression.Property(
                    recordFields,
                    "Item",
                    LinqExpression.Constant(fieldIndex));
                arguments[i] = LinqExpression.Convert(
                    ReadSandboxField(sandboxField, fields[fieldIndex].Type),
                    parameters[i].ParameterType);
            }

            var body = LinqExpression.Convert(LinqExpression.New(constructor, arguments), typeof(object));
            return LinqExpression.Lambda<Func<RecordValue, object>>(body, record).Compile();
        }

        private static LinqExpression ReadSandboxField(LinqExpression sandboxField, Type fieldType)
            => ReadSandboxRecordField(sandboxField, fieldType);

        private static Func<object, object?>[] CreateGetters(IReadOnlyList<RecordMember> fields)
        {
            var getters = new Func<object, object?>[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                getters[i] = CreateGetter(fields[i]);
            }

            return getters;
        }

        private static Func<object, object?> CreateGetter(RecordMember member)
        {
            return member.GetValue;
        }

        private static (ConstructorInfo? Constructor, int[] Map) FindConstructor(
            Type type,
            IReadOnlyList<RecordMember> fields)
        {
            // A record's positional constructor sets only its declared members. Derived/get-only members
            // (e.g. `Map => new(Channel, MapId)`) still appear as wire fields but have no constructor parameter,
            // so the parameter count can be a strict subset of the field count. Accept any constructor whose
            // parameters all map to distinct fields, and keep the richest one (the primary constructor) so the
            // most members are assigned and only genuinely derived fields are left to recompute themselves.
            ConstructorInfo? best = null;
            int[] bestMap = [];
            foreach (var constructor in type.GetConstructors())
            {
                var parameters = constructor.GetParameters();
                if (parameters.Length == 0 || parameters.Length > fields.Count)
                {
                    continue;
                }

                var map = new int[parameters.Length];
                var assigned = new bool[fields.Count];
                if (TryMapConstructor(parameters, fields, map, assigned) &&
                    parameters.Length > (best?.GetParameters().Length ?? 0))
                {
                    best = constructor;
                    bestMap = map;
                }
            }

            return (best, bestMap);
        }

        private static bool TryMapConstructor(
            IReadOnlyList<ParameterInfo> parameters,
            IReadOnlyList<RecordMember> fields,
            int[] map,
            bool[] assigned)
        {
            for (var i = 0; i < parameters.Count; i++)
            {
                var fieldIndex = FieldIndex(fields, parameters[i].Name);
                if (fieldIndex < 0 ||
                    assigned[fieldIndex] ||
                    parameters[i].ParameterType != fields[fieldIndex].Type)
                {
                    return false;
                }

                map[i] = fieldIndex;
                assigned[fieldIndex] = true;
            }

            return true;
        }

        private static int FieldIndex(IReadOnlyList<RecordMember> fields, string? name)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                if (string.Equals(fields[i].Name, name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            var match = -1;
            for (var i = 0; i < fields.Count; i++)
            {
                if (!string.Equals(fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (match >= 0)
                {
                    return -1;
                }

                match = i;
            }

            return match;
        }
    }
}
