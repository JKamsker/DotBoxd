using System.Reflection;
using System.Text;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Hosting.Execution;

internal readonly record struct HostServiceBindingRouteSignature(string Value)
{
    private const int MaxDepth = 8;

    public static HostServiceBindingRouteSignature ForMethod(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var returnType = HostServiceBindingFactory.UnwrapReturnType(method.ReturnType);
        return new HostServiceBindingRouteSignature(ContractSignature(parameters, returnType));
    }

    public static HostServiceBindingRouteSignature ForHandle(
        MethodInfo factoryMethod,
        MethodInfo handleMethod)
    {
        var parameters = factoryMethod.GetParameters().Concat(handleMethod.GetParameters());
        var returnType = HostServiceBindingFactory.UnwrapReturnType(handleMethod.ReturnType);
        return new HostServiceBindingRouteSignature(ContractSignature(parameters, returnType));
    }

    public static HostServiceBindingRouteSignature ForProperty(PropertyInfo property)
        => new(
            "property:" +
            (property.DeclaringType?.FullName ?? "<unknown>") +
            "." +
            property.Name +
            ";" +
            ContractSignature([], HostServiceBindingFactory.UnwrapReturnType(property.PropertyType)));

    public bool Matches(HostServiceBindingRouteSignature other)
        => string.Equals(Value, other.Value, StringComparison.Ordinal);

    private static string ContractSignature(IEnumerable<ParameterInfo> parameters, Type? returnType)
    {
        var builder = new StringBuilder();
        builder.Append("params(");
        var first = true;
        foreach (var parameter in parameters)
        {
            if (!first)
            {
                builder.Append(',');
            }

            builder.Append(TypeSignature(parameter.ParameterType, 0));
            first = false;
        }

        builder.Append(")->");
        builder.Append(returnType is null ? "void" : TypeSignature(returnType, 0));
        return builder.ToString();
    }

    private static string TypeSignature(Type type, int depth)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return "nullable<" + TypeSignature(underlying, depth + 1) + ">";
        }

        if (TryGetElementType(type, out var elementType))
        {
            return "list<" + TypeSignature(elementType, depth + 1) + ">";
        }

        if (TryGetMapTypes(type, out var keyType, out var valueType))
        {
            return "map<" + TypeSignature(keyType, depth + 1) + "," + TypeSignature(valueType, depth + 1) + ">";
        }

        if (depth < MaxDepth && TryGetDtoMembers(type, out var members))
        {
            var builder = new StringBuilder("record<");
            builder.Append(type.FullName ?? type.Name);
            builder.Append(">(");
            for (var i = 0; i < members.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(members[i].Name);
                builder.Append(':');
                builder.Append(TypeSignature(members[i].Type, depth + 1));
            }

            builder.Append(')');
            return builder.ToString();
        }

        return type.FullName ?? type.Name;
    }

    private static bool TryGetElementType(Type type, out Type elementType)
    {
        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return type.GetArrayRank() == 1;
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(List<>) ||
                definition == typeof(IReadOnlyList<>) ||
                definition == typeof(IList<>) ||
                definition == typeof(IEnumerable<>) ||
                definition == typeof(IReadOnlyCollection<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }
        }

        elementType = null!;
        return false;
    }

    private static bool TryGetMapTypes(Type type, out Type keyType, out Type valueType)
    {
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(Dictionary<,>) ||
                definition == typeof(IReadOnlyDictionary<,>) ||
                definition == typeof(IDictionary<,>))
            {
                var arguments = type.GetGenericArguments();
                keyType = arguments[0];
                valueType = arguments[1];
                return true;
            }
        }

        keyType = null!;
        valueType = null!;
        return false;
    }

    private static bool TryGetDtoMembers(Type type, out RouteSignatureMember[] members)
    {
        if (IsNonDto(type) || ImplementsGenericEnumerable(type))
        {
            members = [];
            return false;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var collected = new List<RouteSignatureMember>();
        var properties = type.GetProperties(flags);
        Array.Sort(properties, static (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
        foreach (var property in properties)
        {
            if (property.GetMethod is { IsPublic: true } &&
                property.GetIndexParameters().Length == 0 &&
                !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal) &&
                !KernelRpcMarshaller.IsIgnoredMember(property))
            {
                collected.Add(new RouteSignatureMember(property.Name, property.PropertyType));
            }
        }

        var fields = type.GetFields(flags);
        Array.Sort(fields, static (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
        foreach (var field in fields)
        {
            if (!KernelRpcMarshaller.IsIgnoredMember(field))
            {
                collected.Add(new RouteSignatureMember(field.Name, field.FieldType));
            }
        }

        members = [.. collected];
        return members.Length > 0;
    }

    private static bool IsNonDto(Type type)
        => type == typeof(string) ||
           type == typeof(DateTime) ||
           type == typeof(DateTimeOffset) ||
           type == typeof(TimeSpan) ||
           type == typeof(DateOnly) ||
           type == typeof(TimeOnly) ||
           type == typeof(Index) ||
           type == typeof(Range) ||
           type == typeof(CancellationToken) ||
           type.IsPrimitive ||
           type.IsEnum ||
           TryGetElementType(type, out _) ||
           TryGetMapTypes(type, out _, out _) ||
           !(type.IsClass || type.IsValueType);

    private static bool ImplementsGenericEnumerable(Type type)
    {
        foreach (var @interface in type.GetInterfaces())
        {
            if (@interface.IsGenericType &&
                @interface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct RouteSignatureMember(string Name, Type Type);
}
