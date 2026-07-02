using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerWorldExtensionSuffixResolver
{
    internal static string Resolve(
        Compilation compilation,
        INamedTypeSymbol worldType,
        CancellationToken cancellationToken)
    {
        var services = CollectServices(compilation, cancellationToken);
        var targetKey = ServiceKey(
            NamespaceName(worldType.ContainingNamespace),
            worldType.Name,
            ServiceName(worldType));

        return BuildExtensionSuffixes(services, cancellationToken).TryGetValue(targetKey, out var suffix)
            ? suffix
            : StripInterfacePrefix(worldType.Name);
    }

    private static List<ServiceExtensionCandidate> CollectServices(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var services = new List<ServiceExtensionCandidate>();
        CollectServices(compilation.GlobalNamespace, services, cancellationToken);
        services.Sort(static (left, right) =>
        {
            var ns = string.Compare(left.Namespace, right.Namespace, StringComparison.Ordinal);
            if (ns != 0)
            {
                return ns;
            }

            var interfaceName = string.Compare(left.InterfaceName, right.InterfaceName, StringComparison.Ordinal);
            return interfaceName != 0
                ? interfaceName
                : string.Compare(left.ServiceName, right.ServiceName, StringComparison.Ordinal);
        });

        return services;
    }

    private static void CollectServices(
        INamespaceSymbol ns,
        List<ServiceExtensionCandidate> services,
        CancellationToken cancellationToken)
    {
        foreach (var nested in ns.GetNamespaceMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            CollectServices(nested, services, cancellationToken);
        }

        foreach (var type in ns.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type.TypeKind == TypeKind.Interface &&
                type.ContainingType is null &&
                type.Locations.Any(static location => location.IsInSource) &&
                IsDotBoxDService(type))
            {
                services.Add(new ServiceExtensionCandidate(
                    NamespaceName(type.ContainingNamespace),
                    type.Name,
                    ServiceName(type)));
            }
        }
    }

    private static Dictionary<string, string> BuildExtensionSuffixes(
        IReadOnlyList<ServiceExtensionCandidate> services,
        CancellationToken cancellationToken)
    {
        var shortNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var service in services)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shortName = StripInterfacePrefix(service.InterfaceName);
            shortNameCounts.TryGetValue(shortName, out var count);
            shortNameCounts[shortName] = count + 1;
        }

        var suffixes = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var service in services)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var serviceName = StripInterfacePrefix(service.InterfaceName);
            var suffix = shortNameCounts[serviceName] > 1
                ? QualifiedSuffix(service, serviceName)
                : serviceName;
            var candidate = suffix;
            var disambiguator = 1;
            while (!used.Add(candidate))
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidate = suffix + "__" + disambiguator.ToString(System.Globalization.CultureInfo.InvariantCulture);
                disambiguator++;
            }

            suffixes[ServiceKey(service.Namespace, service.InterfaceName, service.ServiceName)] = candidate;
        }

        return suffixes;
    }

    private static string ServiceName(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (!DotBoxDMetadataNames.IsRpcServiceAttribute(attribute.AttributeClass?.ToDisplayString()))
            {
                continue;
            }

            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (string.Equals(namedArgument.Key, "Name", StringComparison.Ordinal) &&
                    namedArgument.Value.Value is string configuredName)
                {
                    return configuredName;
                }
            }
        }

        return type.Name;
    }

    private static bool IsDotBoxDService(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (DotBoxDMetadataNames.IsRpcServiceAttribute(attribute.AttributeClass?.ToDisplayString()))
            {
                return true;
            }
        }

        return false;
    }

    private static string QualifiedSuffix(ServiceExtensionCandidate service, string serviceName)
        => string.IsNullOrEmpty(service.Namespace)
            ? serviceName
            : NamespaceIdentifierPrefix(service.Namespace) + "_" + serviceName;

    private static string NamespaceIdentifierPrefix(string namespaceName)
    {
        var normalized = namespaceName.Replace("@", "");
        var flattened = normalized.Replace('.', '_');
        if (normalized.IndexOf('_') < 0)
        {
            return flattened;
        }

        return flattened + "__" + StableHash(normalized);
    }

    private static string StableHash(string value)
    {
        unchecked
        {
            ulong hash = 14695981039346656037;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 1099511628211;
            }

            return hash.ToString("x16");
        }
    }

    private static string ServiceKey(string @namespace, string interfaceName, string serviceName)
        => @namespace + "\u001f" + interfaceName + "\u001f" + serviceName;

    private static string NamespaceName(INamespaceSymbol ns)
        => ns.IsGlobalNamespace ? string.Empty : ns.ToDisplayString();

    private static string StripInterfacePrefix(string interfaceName)
    {
        if (interfaceName.Length > 1 && interfaceName[0] == 'I' && char.IsUpper(interfaceName[1]))
        {
            return interfaceName.Substring(1);
        }

        return interfaceName;
    }

    private sealed record ServiceExtensionCandidate(
        string Namespace,
        string InterfaceName,
        string ServiceName);
}
