using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;
using DotBoxD.Plugins.Runtime.Validation;

namespace DotBoxD.Plugins.Runtime;

internal static class LocalTerminalManifestValidator
{
    public static void ValidateRunLocal<TProjected>(PluginPackage package)
    {
        var subscription = package.Manifest.Subscriptions.Count > 0 ? package.Manifest.Subscriptions[0] : null;
        if (subscription is not { LocalTerminal: true })
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' does not declare localTerminal metadata.");
        }

        if (subscription.ProjectedType is null)
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' does not declare projectedType metadata.");
        }

        if (!ProjectedTypeMatches(subscription.ProjectedType, typeof(TProjected)))
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' projectedType '{subscription.ProjectedType}' does not match " +
                $"handler type '{typeof(TProjected).FullName ?? typeof(TProjected).Name}'.");
        }

        ValidateHandleReturnType<TProjected>(package);
    }

    private static void ValidateHandleReturnType<TProjected>(PluginPackage package)
    {
        if (!PluginEntrypointIndex.Build(package).TryGet(package.Entrypoints.Handle, out var handle) ||
            handle.ReturnType == SandboxType.Unit)
        {
            return;
        }

        var expected = KernelRpcMarshaller.SandboxTypeOf(typeof(TProjected));
        if (handle.ReturnType != expected)
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' projectedType '{package.Manifest.Subscriptions[0].ProjectedType}' " +
                $"declares handler type '{typeof(TProjected).FullName ?? typeof(TProjected).Name}', but Handle returns " +
                $"'{handle.ReturnType}' instead of '{expected}'.");
        }
    }

    private static bool ProjectedTypeMatches(string declared, Type expected)
        => declared switch
        {
            "bool" => expected == typeof(bool),
            "int" => expected == typeof(int) || IsEnum(expected),
            "long" => expected == typeof(long) || IsEnum(expected),
            "double" => expected == typeof(double) || expected == typeof(float),
            "string" => expected == typeof(string),
            "guid" => expected == typeof(Guid),
            "list" => expected != typeof(string) &&
                !IsMap(expected) &&
                typeof(System.Collections.IEnumerable).IsAssignableFrom(expected),
            "map" => IsMap(expected),
            "record" => IsAnonymousType(expected) || IsFrameworkRecordType(expected),
            _ => TypeNameMatches(declared, expected)
        };

    private static bool IsEnum(Type type)
        => type.IsEnum;

    private static bool IsMap(Type type)
        => typeof(System.Collections.IDictionary).IsAssignableFrom(type) ||
           IsGenericMap(type) ||
           type.GetInterfaces().Any(IsGenericMap);

    private static bool IsGenericMap(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        // A RunLocal map projection whose handler parameter is declared as IReadOnlyDictionary<,> (or a
        // Dictionary<,>, which implements both) is treated as a map by the generator/RPC mapper; accept it here
        // too so the decoder's materialized Dictionary is not rejected before the callback is even registered.
        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(IDictionary<,>) ||
               definition == typeof(IReadOnlyDictionary<,>);
    }

    private static bool TypeNameMatches(string declared, Type expected)
    {
        var normalized = Normalize(declared);
        return string.Equals(normalized, Normalize(CSharpTypeName(expected)), StringComparison.Ordinal) ||
               string.Equals(normalized, Normalize(expected.FullName ?? expected.Name), StringComparison.Ordinal);
    }

    private static bool IsAnonymousType(Type type)
        => Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute), inherit: false) &&
           type.IsGenericType &&
           (type.Name.StartsWith("<>", StringComparison.Ordinal) ||
            type.Name.StartsWith("VB$", StringComparison.Ordinal)) &&
           type.Name.Contains("AnonymousType", StringComparison.Ordinal) &&
           !type.IsPublic &&
           !type.IsNestedPublic;

    private static bool IsFrameworkRecordType(Type type)
        => type == typeof(DateTime) ||
           type == typeof(decimal) ||
           type == typeof(Index) ||
           type == typeof(Range);

    private static string CSharpTypeName(Type type)
    {
        if (type.IsArray)
        {
            return CSharpTypeName(type.GetElementType()!) + "[]";
        }

        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var definitionName = type.GetGenericTypeDefinition().FullName ?? type.Name;
        var tick = definitionName.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
        {
            definitionName = definitionName[..tick];
        }

        return definitionName + "<" +
               string.Join(", ", type.GetGenericArguments().Select(CSharpTypeName)) +
               ">";
    }

    private static string Normalize(string name)
    {
        const string globalPrefix = "global::";
        return (name.StartsWith(globalPrefix, StringComparison.Ordinal)
                ? name[globalPrefix.Length..]
                : name)
            .Replace('+', '.');
    }
}
