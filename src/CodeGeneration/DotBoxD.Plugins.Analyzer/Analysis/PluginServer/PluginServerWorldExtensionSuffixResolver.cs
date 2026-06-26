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
        // Prefer the suffix that the services source generator already emitted into
        // DotBoxDGeneratedExtensions. When the [DotBoxDService] interfaces live in a referenced assembly the
        // in-source collision count sees none of them and would fall back to Get{ShortName} — but the referenced
        // assembly emitted a disambiguated Get{Combat_Access}. Mirror the Provide{suffix} resolver and bind to
        // the Get extension that actually exists so the facade never references a dangling method.
        if (TryResolveFromGeneratedExtensions(compilation, worldType) is { } generatedSuffix)
        {
            return generatedSuffix;
        }

        var services = CollectServices(compilation, cancellationToken);
        var targetKey = ServiceKey(
            NamespaceName(worldType.ContainingNamespace),
            worldType.Name,
            ServiceName(worldType));

        return BuildExtensionSuffixes(services, cancellationToken).TryGetValue(targetKey, out var suffix)
            ? suffix
            : StripInterfacePrefix(worldType.Name);
    }

    // The Get{suffix} extension is generated into DotBoxDGeneratedExtensions by the services source generator in
    // the assembly that declares the world interface. GetTypeByMetadataName returns null when that type is absent
    // (same-assembly: still being generated) or ambiguous, in which case the caller falls back to the in-source
    // suffix computation. When present, the single static Get* method that returns the world type names the suffix.
    private static string? TryResolveFromGeneratedExtensions(
        Compilation compilation,
        INamedTypeSymbol worldType)
    {
        var extensions = compilation.GetTypeByMetadataName("DotBoxD.Services.Generated.DotBoxDGeneratedExtensions");
        var rpcPeerType = compilation.GetTypeByMetadataName("DotBoxD.Services.Peer.RpcPeer");
        if (extensions is null || rpcPeerType is null)
        {
            return null;
        }

        string? resolved = null;
        foreach (var member in extensions.GetMembers())
        {
            if (member is not IMethodSymbol
                {
                    IsStatic: true,
                    IsGenericMethod: false,
                    Parameters.Length: 1,
                    Name.Length: > 3
                } method ||
                !method.Name.StartsWith("Get", StringComparison.Ordinal) ||
                !SymbolEqualityComparer.Default.Equals(method.ReturnType, worldType) ||
                !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, rpcPeerType))
            {
                continue;
            }

            var candidate = method.Name.Substring(3);
            if (resolved is not null && !string.Equals(resolved, candidate, StringComparison.Ordinal))
            {
                // Genuinely ambiguous (two Get extensions return the same world type): fall back rather than
                // guessing, so the in-source computation or the disambiguated suffix wins deterministically.
                return null;
            }

            resolved = candidate;
        }

        return resolved;
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
            if (!string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.DotBoxDServiceAttribute,
                    StringComparison.Ordinal))
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
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.DotBoxDServiceAttribute,
                    StringComparison.Ordinal))
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
