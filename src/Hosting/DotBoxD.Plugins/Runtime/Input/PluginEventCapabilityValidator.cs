using System.Collections.Concurrent;
using System.Reflection;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Plugins.Runtime.Input;

internal static class PluginEventCapabilityValidator
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, string>> CapabilityParameters = new();

    public static void Validate<TEvent>(
        ExecutionPlan plan,
        KernelEntrypoints entrypoints,
        IReadOnlyList<Parameter> parameters)
    {
        var required = RequiredCapabilities<TEvent>(parameters);
        if (required.Count == 0)
        {
            return;
        }

        var declared = new HashSet<string>(
            plan.GetEntrypointMetadata(entrypoints.ShouldHandle).RequiredCapabilities,
            StringComparer.Ordinal);
        declared.UnionWith(plan.GetEntrypointMetadata(entrypoints.Handle).RequiredCapabilities);

        var missing = required
            .Where(capability => !declared.Contains(capability))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        throw new SandboxValidationException([
            new SandboxDiagnostic(
                "DBXK044",
                "Plugin manifest requiredCapabilities do not match verified entrypoint capabilities " +
                $"(missing: {string.Join(", ", missing)}).")
        ]);
    }

    private static IReadOnlySet<string> RequiredCapabilities<TEvent>(IReadOnlyList<Parameter> parameters)
    {
        var parameterCapabilities = CapabilityParameters.GetOrAdd(
            typeof(TEvent),
            static eventType => BuildCapabilityParameters(eventType));
        if (parameterCapabilities.Count == 0)
        {
            return EmptyCapabilities.Set;
        }

        var required = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < parameters.Count; i++)
        {
            if (parameterCapabilities.TryGetValue(parameters[i].Name, out var capability))
            {
                required.Add(capability);
            }
        }

        return required;
    }

    private static IReadOnlyDictionary<string, string> BuildCapabilityParameters(Type eventType)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in ReadablePropertiesInHierarchy(eventType))
        {
            var capability = property.GetCustomAttribute<CapabilityAttribute>(inherit: false)?.Id;
            if (!string.IsNullOrWhiteSpace(capability))
            {
                result[PluginManifestNames.EventParameters.Prefix + property.Name] = capability;
            }
        }

        return result;
    }

    private static IEnumerable<PropertyInfo> ReadablePropertiesInHierarchy(Type eventType)
    {
        for (var current = eventType; current is not null && current != typeof(object); current = current.BaseType)
        {
            var properties = current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            for (var i = 0; i < properties.Length; i++)
            {
                var property = properties[i];
                if (property.GetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0)
                {
                    yield return property;
                }
            }
        }
    }

    private static class EmptyCapabilities
    {
        public static readonly IReadOnlySet<string> Set = new HashSet<string>(StringComparer.Ordinal);
    }
}
